# Assistente de Impressao (C# / WPF / .NET 8)

Ferramenta com interface grafica para diagnosticar e corrigir de forma automatizada os
erros de impressao 0x0000005B, 0x000004F8 e 0x0000011B, com minima interacao humana.

## Requisitos

- Windows 10/11
- .NET 8 SDK (https://dotnet.microsoft.com/download/dotnet/8.0)
- Visual Studio 2022 (17.8+) com workload ".NET desktop development", OU dotnet CLI

## Como compilar e executar

### Opcao 1: Visual Studio
1. Abra `PrinterTool.csproj` no Visual Studio 2022.
2. Defina a configuracao para `Debug` ou `Release`.
3. Pressione F5. O Windows solicitara elevacao (UAC) porque o `app.manifest`
   exige execucao como Administrador — isso e necessario para alterar o Registro.

### Opcao 2: linha de comando
```
cd PrinterTool
dotnet build -c Release
dotnet run -c Release
```
Para gerar um executavel unico distribuivel:
```
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```
O executavel sera gerado em `bin\Release\net8.0-windows\win-x64\publish\`.

## O que a ferramenta faz

- **Rodar Diagnostico**: verifica servicos criticos (Spooler, RPC, DCOM, LanmanServer/Workstation),
  impressoras instaladas, rede (ping, DNS, portas 445/139, UNC), firewall e eventos recentes
  do PrintService. Nao altera nada no sistema.
- **Aplicar Correcoes RPC (0x0000011B)**: cria backup automatico do Registro, aplica 5 valores
  relacionados a RPC/Point-and-Print/Terminal Services/isolamento de driver, e revalida cada
  valor apos a escrita. Requer Administrador.
- **Corrigir Automaticamente Tudo**: roda diagnostico, valida dependencias do Spooler, limpa
  fila travada, aplica correcoes RPC, reinicia o Spooler com seguranca, e roda o diagnostico
  novamente para confirmar a resolucao. Fluxo pensado para minima interacao humana.
- **Correcoes Manuais**: acoes pontuais (reiniciar spooler, limpar fila, verificar drivers
  orfaos, validar dependencias, validar arquitetura de driver) para quando o diagnostico
  apontar um problema especifico.
- **Painel de Saude**: 10 indicadores (Servicos, Rede, RPC, SMB, Firewall, Impressoras, Drivers,
  DNS, Hostname, Compartilhamentos) com evidencia, causa provavel e sugestao de correcao.
- **Log Tecnico**: toda operacao e registrada com nivel, categoria, resultado, chave de
  Registro alterada (valor antigo -> novo) e duracao. Exportavel em HTML.

## Sobre os backups de Registro

Antes de qualquer alteracao, a ferramenta salva um snapshot em JSON em:
`%ProgramData%\PrinterTool\Backups\`
Esses arquivos permitem reverter manualmente qualquer valor alterado, mesmo que a interface
seja fechada.

## Sobre os logs

Logs tecnicos ficam em: `%ProgramData%\PrinterTool\Logs\`
Retencao automatica de 30 dias.

## Observacoes importantes

- A ferramenta **precisa ser executada como Administrador** para aplicar qualquer correcao
  de Registro ou reiniciar servicos. Sem isso, o diagnostico funciona, mas as correcoes sao
  bloqueadas com aviso explicito.
- O erro 0x0000011B esta associado a politicas de RPC/hardening de impressao introduzidas em
  atualizacoes de seguranca do Windows; a correcao aplicada endereca especificamente esse cenario.
- Os erros 0x0000005B e 0x000004F8 normalmente estao ligados a spooler, fila travada, drivers
  ausentes/incompativeis ou conectividade com o servidor — cobertos pelo diagnostico e pelas
  correcoes inteligentes.
