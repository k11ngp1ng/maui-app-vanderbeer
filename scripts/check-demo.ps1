param([string]$BaseUrl = 'http://127.0.0.1:5080')

$ErrorActionPreference = 'Stop'
$base = $BaseUrl.TrimEnd('/')

function Require([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
}

$health = Invoke-RestMethod "$base/health"
Require ($health.status -eq 'Healthy') 'A API não está saudável.'

$categories = Invoke-RestMethod "$base/api/v1/categories"
Require (@($categories | Where-Object name -eq 'Cervejas').Count -eq 1) 'Categoria Cervejas ausente.'
Require (@($categories | Where-Object name -eq 'Kits').Count -eq 1) 'Categoria Kits ausente.'

$payments = Invoke-RestMethod "$base/api/v1/payment-methods"
foreach ($code in @('Cash', 'CardOnFulfillment', 'PixOnFulfillment')) {
    Require (@($payments | Where-Object code -eq $code).Count -eq 1) "Forma de pagamento $code ausente."
}

$settings = Invoke-RestMethod "$base/api/v1/store-settings"
Require (-not [string]::IsNullOrWhiteSpace($settings.today)) 'Data comercial indisponível.'

$page = Invoke-RestMethod "$base/api/v1/products"
$pilsen = @($page.items | Where-Object sku -eq 'VB-PILSEN-350')
$kit = @($page.items | Where-Object sku -eq 'VB-KIT-3')
Require ($pilsen.Count -eq 1 -and [decimal]$pilsen[0].price -eq 10.00) 'Pilsen não está no preço do seed (R$ 10,00).'
Require ($kit.Count -eq 1 -and [decimal]$kit[0].price -eq 29.90) 'Kit não está no preço do seed (R$ 29,90).'
Require ($pilsen[0].isAvailable -and $kit[0].isAvailable) 'Um produto do seed está indisponível.'

Write-Output 'Prévia aprovada: API saudável; 2 categorias, 3 formas de pagamento e 2 produtos disponíveis com preços do seed.'
