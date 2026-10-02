#Requires -RunAsAdministrator
<#
    Configura as variáveis de ambiente do FinancialImport usando um PREFIXO próprio
    ("FinancialImport_"), isolando este app das variáveis genéricas (sem prefixo) que
    outros sistemas no mesmo servidor definem — por exemplo, o PortalFiscalHub define
    uma variável de MÁQUINA "ConnectionStrings__DefaultConnection" apontando para o
    banco dele, que antes sequestrava a conexão do FinancialImport.

    O app FinancialImport lê a configuração com AddEnvironmentVariables("FinancialImport_"),
    então o prefixo é removido ao ler:
        FinancialImport_ConnectionStrings__FinancialImport  ->  ConnectionStrings:FinancialImport

    Rode uma vez no servidor, como Administrador. Depois reinicie o app/serviço.
#>

$p = 'FinancialImport_'

function Set-Var($n, $v) { [Environment]::SetEnvironmentVariable("$p$n", $v, "Machine") }

function Read-Secret($prompt) {
    $s = Read-Host $prompt -AsSecureString
    $b = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($s)
    try { [Runtime.InteropServices.Marshal]::PtrToStringBSTR($b) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($b) }
}

# ── Banco de dados do FinancialImport ───────────────────────────────────────
# Observação: o nome da chave interna é "FinancialImport" (e NÃO "DefaultConnection")
# de propósito — assim a variável genérica de outro portal nunca consegue sequestrar
# a conexão deste app, mesmo que esta variável aqui falte.
$senha = Read-Secret "Senha do usuario MySQL 'root'"
Set-Var "ConnectionStrings__FinancialImport" "Server=localhost;Database=financialimport;User=root;Password=$senha;Port=3306;"
Remove-Variable senha

# ── Conferência (mostra o Database/Server, nunca a senha) ────────────────────
$cs = [Environment]::GetEnvironmentVariable("${p}ConnectionStrings__FinancialImport", "Machine")
if ($cs) {
    $safe = ($cs -replace 'Password=[^;]*', 'Password=***')
    Write-Host "OK -> ${p}ConnectionStrings__FinancialImport = $safe"
} else {
    Write-Host "ERRO: a variavel nao foi criada."
}

Write-Host ""
Write-Host "Feito. REINICIE o app/servico FinancialImport para que ele leia a nova configuracao."
Write-Host "(Variavel de MAQUINA so e lida por processos iniciados DEPOIS de defini-la — feche e reabra o terminal/servico.)"
