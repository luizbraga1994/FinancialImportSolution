#Requires -RunAsAdministrator
<#
    Configura as variaveis de ambiente do FinancialImport usando um PREFIXO proprio
    ("FinancialImport_"), isolando este app das variaveis genericas (sem prefixo) que
    outros sistemas no mesmo servidor definem - por exemplo, o PortalFiscalHub define
    uma variavel de MAQUINA "ConnectionStrings__DefaultConnection" apontando para o
    banco dele, que antes sequestrava a conexao do FinancialImport.

    O app FinancialImport le a configuracao com AddEnvironmentVariables("FinancialImport_"),
    entao o prefixo e removido ao ler:
        FinancialImport_ConnectionStrings__FinancialImport  ->  ConnectionStrings:FinancialImport

    Rode uma vez no servidor, como Administrador. Depois reinicie o app/servico.
#>

$ErrorActionPreference = 'Stop'
$prefix = 'FinancialImport_'

function Set-Var($name, $value) {
    [Environment]::SetEnvironmentVariable("$prefix$name", $value, "Machine")
}

function Read-Secret($prompt) {
    $s = Read-Host $prompt -AsSecureString
    $b = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($s)
    try { [Runtime.InteropServices.Marshal]::PtrToStringBSTR($b) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($b) }
}

# Banco de dados do FinancialImport.
# A chave interna e "FinancialImport" (e NAO "DefaultConnection") de proposito -
# assim a variavel generica de outro portal nunca sequestra a conexao deste app.
$senha = Read-Secret "Senha do usuario MySQL 'root'"
Set-Var "ConnectionStrings__FinancialImport" "Server=localhost;Database=financialimport;User=root;Password=$senha;Port=3306;"
Remove-Variable senha

# Conferencia (mostra o Database/Server, nunca a senha)
$cs = [Environment]::GetEnvironmentVariable("${prefix}ConnectionStrings__FinancialImport", "Machine")
if ($cs) {
    $safe = ($cs -replace 'Password=[^;]*', 'Password=***')
    Write-Host "OK -> ${prefix}ConnectionStrings__FinancialImport = $safe"
} else {
    Write-Host "ERRO: a variavel nao foi criada."
}

Write-Host ""
Write-Host "Feito. REINICIE o app/servico FinancialImport para ele ler a nova configuracao."
Write-Host "Obs: variavel de MAQUINA so e lida por processos iniciados DEPOIS de defini-la."
Write-Host "Feche e reabra o terminal/servico."
