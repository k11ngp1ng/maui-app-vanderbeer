param([string]$BaseUrl = 'http://127.0.0.1:5080')

$ErrorActionPreference = 'Stop'
$base = $BaseUrl.TrimEnd('/')
$examples = (Get-Content (Join-Path $PSScriptRoot '../docs/api/examples.json') -Raw -Encoding UTF8 | ConvertFrom-Json).cases
function Example([string]$name) { return ($examples | Where-Object name -eq $name | Select-Object -First 1).value }
function Send([string]$method, [string]$path, $body) {
    $params = @{ Method = $method; Uri = "$base$path"; ContentType = 'application/json'; UseBasicParsing = $true }
    if ($null -ne $body) { $params.Body = [System.Text.Encoding]::UTF8.GetBytes(($body | ConvertTo-Json -Depth 12 -Compress)) }
    try { $response = Invoke-WebRequest @params }
    catch [System.Net.WebException] {
        if ($null -eq $_.Exception.Response) { throw }
        $response = $_.Exception.Response
        $reader = [System.IO.StreamReader]::new($response.GetResponseStream())
        try { $content = $reader.ReadToEnd() } finally { $reader.Dispose() }
        return @{ Status = [int]$response.StatusCode; Body = ($content | ConvertFrom-Json); Headers = $response.Headers }
    }
    return @{ Status = [int]$response.StatusCode; Body = ($response.Content | ConvertFrom-Json); Headers = $response.Headers }
}
function Check($condition, [string]$message) { if (-not $condition) { throw $message } }
function New-Cpf {
    $digits = @(1..9 | ForEach-Object { Get-Random -Minimum 0 -Maximum 10 })
    for ($length = 9; $length -le 10; $length++) {
        $sum = 0
        for ($i = 0; $i -lt $length; $i++) { $sum += $digits[$i] * ($length + 1 - $i) }
        $remainder = ($sum * 10) % 11
        $digits += $(if ($remainder -eq 10) { 0 } else { $remainder })
    }
    return ($digits -join '')
}

$productId = '00000000-0000-4000-8000-000000000009'
$product = (Send GET "/api/v1/admin/products/$productId" $null).Body
Check ($product.isActive -and $product.isAvailable) 'O produto do seed deve estar ativo e disponível.'
$original = $product
try {
    $customerRequest = Example 'CreateCustomer-request'
    $customerRequest.clientRequestId = [guid]::NewGuid().ToString()
    $customerRequest.cpf = New-Cpf
    $customerResponse = Send POST '/api/v1/customers' $customerRequest
    Check ($customerResponse.Status -eq 201) "Cadastro: HTTP $($customerResponse.Status)"
    $customer = $customerResponse.Body

    $settings = (Send GET '/api/v1/store-settings' $null).Body
    $orderRequest = Example 'CreateOrder-request'
    $orderRequest.clientRequestId = [guid]::NewGuid().ToString()
    $orderRequest.customerId = $customer.id
    $orderRequest.expectedCustomerVersion = $customer.version
    $orderRequest.fulfillmentType = 'Pickup'
    $orderRequest.requestedDate = $settings.today
    $orderRequest.deliveryAddress = $null
    $orderRequest.cashTendered = $null
    $orderRequest.items = @(@{ productId = $productId; quantity = 1; expectedUnitPrice = $original.price })
    $orderResponse = Send POST '/api/v1/orders' $orderRequest
    Check ($orderResponse.Status -eq 201) "Pedido: HTTP $($orderResponse.Status)"
    $order = $orderResponse.Body

    $update = @{
        clientRequestId = [guid]::NewGuid().ToString(); expectedVersion = $original.version
        categoryId = $original.categoryId; sku = $original.sku; name = $original.name
        description = $original.description; unitLabel = $original.unitLabel
        price = ([decimal]$original.price + [decimal]1.00); imageUrl = $original.imageUrl
        isAvailable = $true; isActive = $true
    }
    $changed = Send PUT "/api/v1/admin/products/$productId" $update
    Check ($changed.Status -eq 200) "Preco: HTTP $($changed.Status) $($changed.Body | ConvertTo-Json -Compress -Depth 5)"
    $current = (Send GET "/api/v1/products/$productId" $null).Body
    Check ($current.price -eq $update.price) 'Preço atual não apareceu no catálogo.'

    $historical = (Send GET "/api/v1/orders/$($order.id)" $null).Body
    Check ($historical.total -eq $original.price) 'Preço histórico foi alterado.'
    Check ($historical.items[0].unitPrice -eq $original.price) 'Snapshot do item foi alterado.'
    $replay = Send POST '/api/v1/orders' $orderRequest
    Check ($replay.Status -in @(200, 201) -and $replay.Body.id -eq $order.id) "Replay nao recuperou pedido: HTTP $($replay.Status) $($replay.Body | ConvertTo-Json -Compress -Depth 3)"

    $orderRequest.clientRequestId = [guid]::NewGuid().ToString()
    $rejectedRequest = $orderRequest | ConvertTo-Json -Depth 12 | ConvertFrom-Json
    $rejected = Send POST '/api/v1/orders' $orderRequest
    Check ($rejected.Status -eq 409 -and $rejected.Body.code -eq 'PriceChanged') "Preço antigo não exigiu revisão: HTTP $($rejected.Status), $($rejected.Body.code)"
    $orderRequest.items[0].expectedUnitPrice = $current.price
    $orderRequest.clientRequestId = [guid]::NewGuid().ToString()
    $newOrder = Send POST '/api/v1/orders' $orderRequest
    Check ($newOrder.Status -eq 201 -and $newOrder.Body.id -ne $order.id) 'Nova encomenda com preço atual falhou.'
    $historicalAgain = (Send GET "/api/v1/orders/$($order.id)" $null).Body
    Check ($historicalAgain.total -eq $original.price) 'Nova encomenda alterou o histórico.'

    $restoreBeforeReplay = @{
        clientRequestId = [guid]::NewGuid().ToString(); expectedVersion = $changed.Body.version
        categoryId = $original.categoryId; sku = $original.sku; name = $original.name
        description = $original.description; unitLabel = $original.unitLabel
        price = $original.price; imageUrl = $original.imageUrl
        isAvailable = $original.isAvailable; isActive = $original.isActive
    }
    $restoredProduct = Send PUT "/api/v1/admin/products/$productId" $restoreBeforeReplay
    Check ($restoredProduct.Status -eq 200) 'Falha ao restaurar o preço antes do replay de rejeição.'
    $rejectedReplay = Send POST '/api/v1/orders' $rejectedRequest
    Check ($rejectedReplay.Status -eq 409 -and $rejectedReplay.Body.code -eq 'PriceChanged') 'Rejeição comercial não foi reproduzida após restaurar o preço.'

    $invalidTransition = Send PATCH "/api/v1/admin/orders/$($order.id)/status" @{
        clientRequestId = [guid]::NewGuid().ToString(); expectedVersion = 1
        status = 'Delivered'; reason = $null
    }
    Check ($invalidTransition.Status -eq 409 -and $invalidTransition.Body.code -eq 'InvalidOrderTransition') 'Transição inválida alterou o atendimento.'
    $confirmed = Send PATCH "/api/v1/admin/orders/$($order.id)/status" @{
        clientRequestId = [guid]::NewGuid().ToString(); expectedVersion = 1
        status = 'Confirmed'; reason = $null
    }
    Check ($confirmed.Status -eq 200 -and $confirmed.Body.version -eq 2) 'Confirmação do atendimento falhou.'
    $stale = Send PATCH "/api/v1/admin/orders/$($order.id)/status" @{
        clientRequestId = [guid]::NewGuid().ToString(); expectedVersion = 1
        status = 'Preparing'; reason = $null
    }
    Check ($stale.Status -eq 409 -and $stale.Body.code -eq 'OrderVersionConflict') 'Versão antiga alterou o atendimento.'

    Write-Output "B80 API/MySQL: aprovado; original=$($order.protocol) R$ $($historical.total); novo=$($newOrder.Body.protocol) R$ $($newOrder.Body.total); PriceChanged persistido; replay=mesmo ID; transicao e versao rejeitadas."
}
finally {
    $latest = Send GET "/api/v1/admin/products/$productId" $null
    if ($latest.Status -eq 200 -and $latest.Body.price -ne $original.price) {
        $restore = @{
            clientRequestId = [guid]::NewGuid().ToString(); expectedVersion = $latest.Body.version
            categoryId = $original.categoryId; sku = $original.sku; name = $original.name
            description = $original.description; unitLabel = $original.unitLabel
            price = $original.price; imageUrl = $original.imageUrl
            isAvailable = $original.isAvailable; isActive = $original.isActive
        }
        $restored = Send PUT "/api/v1/admin/products/$productId" $restore
        Check ($restored.Status -eq 200) 'Falha ao restaurar o preço do seed.'
    }
}
