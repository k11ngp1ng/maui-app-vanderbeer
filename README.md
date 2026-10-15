# VanderBeer

Aplicativo de encomendas desenvolvido em **.NET MAUI para Android e Windows**, com API ASP.NET Core, MySQL no servidor e SQLite no aplicativo.

## Funcionalidades

- **Cliente:** catálogo, cadastro, carrinho, finalização, histórico e recompra.
- **Empresa:** gestão de produtos e atendimento de pedidos no mesmo aplicativo.
- **Sincronização:** pedidos salvos localmente, reenvio após reconexão e recuperação de respostas perdidas sem duplicação.

Projeto individual de Programação Web Mobile. A demonstração utiliza dados fictícios e dispensa login; a forma de pagamento é registrada para o atendimento, sem pagamento online.

## Estado do projeto

M0–M8 concluídos. A reorganização visual B90 permanece em verificação. Os ensaios registrados incluíram API/MySQL, Windows e emulador Android; aparelho Android físico e MySQL 8.4 ainda não foram verificados.

Consulte o [backlog](docs/backlog.md), o [contrato OpenAPI](docs/api/openapi.json) e os [exemplos HTTP](docs/api/examples.json). Os registros detalhados de desenvolvimento são mantidos localmente.

## Executar API e aplicativo

Ambiente usado: Windows com SDK .NET 10.0.401, workloads `android` e `maui-windows`, JDK 21, Android SDK e MySQL 8.0.46. O teste Android foi feito no emulador `VanderBeer_API35` (Android 15); o MySQL 8.4 ainda não foi testado. Todos os comandos abaixo partem da raiz do repositório. Use dados fictícios.

1. Crie um banco vazio `vanderbeer_dev` no MySQL e um usuário de desenvolvimento com permissão nesse banco. O serviço MySQL principal desta máquina não foi alterado na prova; o esquema foi testado em uma instância temporária.
2. No PowerShell que executará a migração e a API, defina a conexão sem salvar a senha no repositório:

   ```powershell
   $dbUser = Read-Host 'Usuário MySQL'
   $dbPassword = Read-Host 'Senha MySQL' -MaskInput
   $env:VANDERBEER_DB_CONNECTION = "Server=127.0.0.1;Port=3306;Database=vanderbeer_dev;User ID=$dbUser;Password=$dbPassword;Connection Timeout=3;"
   ```

3. Aplique o esquema, carregue dados fictícios e inicie a API:

   ```powershell
   dotnet restore VanderBeer.slnx
   dotnet run --project tools/VanderBeer.DbMigrator/VanderBeer.DbMigrator.csproj -- migrate
   dotnet run --project tools/VanderBeer.DbMigrator/VanderBeer.DbMigrator.csproj -- seed
   dotnet run --project src/VanderBeer.Api/VanderBeer.Api.csproj --urls http://127.0.0.1:5080
   ```

   O executor confere o checksum e não reaplica uma migration já registrada. O seed pode ser repetido sem duplicar os registros iniciais. Em outro terminal, `curl.exe http://127.0.0.1:5080/health` deve retornar `{"status":"Healthy"}`; `/api/v1/categories` lista Cervejas e Kits, `/api/v1/payment-methods` lista as três formas de pagamento do seed e `/api/v1/products` lista os produtos ativos. Antes da demonstração, execute a prévia sem gravações:

   ```powershell
   pwsh -NoProfile -File scripts/check-demo.ps1 -BaseUrl http://127.0.0.1:5080
   ```

   A prévia exige os preços originais do seed (Pilsen R$ 10,00 e kit R$ 29,90). O seed não desfaz edições administrativas já feitas.

4. Abra o app Windows em outro terminal:

   ```powershell
   dotnet run --project src/VanderBeer.App/VanderBeer.App.csproj -f net10.0-windows10.0.19041.0
   ```

5. Para a prova no emulador Android, inicie o AVD `VanderBeer_API35` e use o SDK/JDK instalados nesta máquina:

   ```powershell
   $androidSdk = Join-Path $env:LOCALAPPDATA 'Android\Sdk'
   $adb = Join-Path $androidSdk 'platform-tools\adb.exe'
   $emulator = Join-Path $androidSdk 'emulator\emulator.exe'
   Start-Process -FilePath $emulator -ArgumentList @('-avd', 'VanderBeer_API35', '-no-window', '-no-snapshot', '-no-boot-anim', '-gpu', 'swiftshader_indirect', '-port', '5554') -WindowStyle Hidden
   & $adb -s emulator-5554 wait-for-device
   while ((& $adb -s emulator-5554 shell getprop sys.boot_completed).Trim() -ne '1') { Start-Sleep -Seconds 2 }
   dotnet build src/VanderBeer.App/VanderBeer.App.csproj -f net10.0-android -p:EmbedAssembliesIntoApk=true -p:AndroidSdkDirectory='C:\Program Files (x86)\Android\android-sdk' -p:JavaSdkDirectory='C:\Program Files (x86)\Android\openjdk\jdk-21.0.8'
   & $adb -s emulator-5554 install --no-incremental -r 'src/VanderBeer.App/bin/Debug/net10.0-android/br.com.vanderbeer.app-Signed.apk'
   & $adb -s emulator-5554 shell am start -n br.com.vanderbeer.app/crc640dfe6ee05fcc8e7f.MainActivity
   ```

   O app usa `10.0.2.2:5080` como endereço do host no emulador. Em aparelho físico, informe em Conexão, no cabeçalho, o endereço HTTP do PC acessível pela rede. Acesse Meu cadastro → Gestão VanderBeer — demonstração → Produtos para criar ou editar um produto; na área Cliente, atualize o catálogo e confira o novo valor. Com a API temporariamente inacessível, a tela identifica o cache SQLite parcial. Uma alteração administrativa com resposta incerta fica disponível em **Recuperar alteração pendente**, inclusive após reiniciar o app.

Verificações independentes do MAUI, executadas localmente: `dotnet build src/VanderBeer.Api/VanderBeer.Api.csproj --configuration Release`, `dotnet build tools/VanderBeer.DbMigrator/VanderBeer.DbMigrator.csproj --configuration Release`, `dotnet test tests/VanderBeer.ApiTests/VanderBeer.ApiTests.csproj --configuration Release`, `dotnet test tests/VanderBeer.UnitTests/VanderBeer.UnitTests.csproj --configuration Release` e `node scripts/validate-planning.mjs`. O fechamento B83 passou com 56 testes unitários, 8 HTTP sem banco e 1 com MySQL isolado. A [CI inicial](.github/workflows/ci.yml) usa builds/testes sem banco nem workload MAUI no runner; sua execução remota ainda não foi comprovada.

## Verificação da especificação

Executado com sucesso da raiz do repositório, usando Node.js e módulos nativos, sem instalar pacotes:

```powershell
node scripts/validate-planning.mjs
```

Verifica links locais, dependências do backlog, referências do contrato e exemplos válidos/inválidos. Valida somente o subconjunto de schemas usado no projeto; não substitui validação OpenAPI completa nem testes da API, dos bancos ou das plataformas MAUI. Node.js serve à documentação, não é dependência da aplicação .NET.
