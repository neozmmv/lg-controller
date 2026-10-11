# LG Controller

Controle um ar-condicionado LG ThinQ pela bandeja do Windows.

## Requisitos

- Windows 10 ou 11 (o fundo translúcido precisa do Windows 11 22H2 ou mais novo)
- .NET 10 Desktop Runtime
- Um ar-condicionado LG já cadastrado no aplicativo ThinQ

## Primeiro uso

1. Gere um token pessoal em https://connect-pat.lgthinq.com com a sua conta LG.
2. Abra `LgController.exe`, cole o token, informe o país e clique em Conectar.
3. Escolha o aparelho e salve.

O token fica em `%APPDATA%\LgController\settings.json`, criptografado para o seu usuário do Windows.

## Uso

- Clique no ícone da bandeja para abrir o painel.
- Clique com o botão direito para ligar ou desligar, iniciar com o Windows, reconfigurar ou sair.

## Desenvolvimento

```powershell
dotnet test
dotnet run --project src/LgController
dotnet publish src/LgController -c Release -o publish
```

`tools/Probe` é um console para testar a API direto no aparelho. Defina `LG_THINQ_PAT` e rode
`dotnet run --project tools/Probe -- list`.

Erros não tratados vão para `%APPDATA%\LgController\log.txt`.
