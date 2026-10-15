// Manual M3 verification helper: forward local HTTP, then lose the first successful write response.
// Usage: node scripts/response-loss-proxy.mjs [listenPort] [apiPort]
import http from 'node:http';

const listenPort = Number(process.argv[2] ?? 5081);
const apiPort = Number(process.argv[3] ?? 5080);
let dropNextWrite = true;

http.createServer((request, response) => {
  const upstream = http.request({
    hostname: '127.0.0.1', port: apiPort, path: request.url, method: request.method,
    headers: { ...request.headers, host: `127.0.0.1:${apiPort}` },
  }, upstreamResponse => {
    if (dropNextWrite && ['POST', 'PUT', 'PATCH'].includes(request.method) &&
        upstreamResponse.statusCode >= 200 && upstreamResponse.statusCode < 300) {
      dropNextWrite = false;
      upstreamResponse.resume();
      upstreamResponse.on('end', () => {
        console.log(`Dropped ${request.method} response after upstream status ${upstreamResponse.statusCode}`);
        response.destroy();
      });
      return;
    }
    response.writeHead(upstreamResponse.statusCode, upstreamResponse.headers);
    upstreamResponse.pipe(response);
  });
  upstream.on('error', error => {
    console.error(error.message);
    if (!response.headersSent) response.writeHead(502);
    response.end();
  });
  request.pipe(upstream);
}).listen(listenPort, '127.0.0.1', () =>
  console.log(`Response-loss proxy listening on ${listenPort}, API ${apiPort}`));
