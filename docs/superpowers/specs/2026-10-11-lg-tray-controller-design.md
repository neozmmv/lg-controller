# LG Controller — design

Data: 2026-10-11

## Objetivo

Um aplicativo de bandeja do Windows para controlar um ar-condicionado LG Wi-Fi
(ThinQ) sem abrir o app do celular. Uso pessoal, um PC, um aparelho.

Sucesso significa: clicar no ícone da bandeja abre um painel flutuante bonito,
e por ele é possível ligar/desligar, ajustar temperatura, trocar modo e
ventilação, e usar os extras (oscilação, economia, timer), com o aparelho
respondendo de verdade.

## Decisões tomadas

| Tema | Decisão |
|---|---|
| Stack | C# + WPF, .NET 10, um único `.exe` |
| API | ThinQ Connect oficial da LG, via REST, com token pessoal (PAT) |
| Janela | Painel flutuante sem borda, colado à bandeja, fecha ao clicar fora |
| Controles | Ligar/desligar, temperatura, modo, ventilação, oscilação, economia, timer |
| Aparelhos | Um aparelho selecionado na configuração inicial |

## Fora do escopo

- Controlar vários aparelhos ao mesmo tempo
- Agendamentos por horário
- Histórico de consumo de energia
- Atualização em tempo real por push (MQTT)

## Arquitetura

Uma solução com dois projetos:

- `src/LgController` — aplicativo WPF
- `tests/LgController.Tests` — testes xUnit

O aplicativo tem três camadas, com dependência em uma só direção
(`UI → Core → ThinQ`):

### `ThinQ/` — cliente da API

Não conhece UI nem o conceito de ar-condicionado. Trabalha com JSON.

- `ThinQClient(HttpClient http, ThinQCredentials credentials)`
  - `Task<IReadOnlyList<DeviceInfo>> ListDevicesAsync()`
  - `Task<JsonElement> GetProfileAsync(string deviceId)`
  - `Task<JsonElement> GetStateAsync(string deviceId)`
  - `Task ControlAsync(string deviceId, object payload)`
- `ThinQCredentials(string Token, string CountryCode, string ClientId)`
- `DeviceInfo(string Id, string Type, string Alias, string ModelName)`
- `ThinQRegion.FromCountry(string countryCode)` — devolve `kic`, `aic` ou `eic`
- `ThinQException(string code, string message, ThinQErrorKind kind)`

### `Core/` — modelo e controle do ar

- `AcCapabilities` — o que o aparelho suporta, extraído do perfil:
  modos disponíveis, velocidades de vento, faixa de temperatura (mín/máx) e
  passo, unidade, e quais extras existem (oscilação vertical, oscilação
  horizontal, economia, timer de desligamento).
- `AcState` — estado atual: ligado, temperatura ambiente, temperatura alvo,
  modo, velocidade de vento, oscilações, economia, minutos restantes do timer.
- `AcProfileParser` — `JsonElement` do perfil → `AcCapabilities`.
- `AcStateParser` — `JsonElement` do estado → `AcState`.
- `AcCommands` — funções puras que montam o payload de cada ação.
- `AcController` — guarda `AcState` e `AcCapabilities`, faz o polling, aplica
  comandos de forma otimista e desfaz se falharem. Expõe `StateChanged` e
  `ErrorOccurred`.
- `SettingsStore` — lê e grava a configuração em disco.

### `UI/` — bandeja, painel e configuração

- `TrayIcon` — ícone, menu de contexto, desenho dinâmico do ícone.
- `FlyoutWindow` + `FlyoutViewModel` — o painel.
- `SetupWindow` + `SetupViewModel` — primeira execução e reconfiguração.
- `Themes/` — estilos, cores claro/escuro, controles customizados.

O ícone da bandeja usa o pacote `H.NotifyIcon.Wpf`. MVVM com
`CommunityToolkit.Mvvm`.

## API ThinQ Connect

Referência: SDK oficial `thinq-connect/pythinqconnect` (Apache-2.0).

**URL base:** `https://api-{região}.lgthinq.com/`, onde a região vem do país:
`kic` (Ásia-Pacífico), `aic` (Américas, inclui `BR`), `eic` (Europa, Oriente
Médio, África).

**Cabeçalhos em toda requisição:**

| Cabeçalho | Valor |
|---|---|
| `Authorization` | `Bearer {PAT}` |
| `x-country` | código do país, ex. `BR` |
| `x-message-id` | UUID v4 em base64 url-safe, sem os dois `=` finais (22 caracteres) |
| `x-client-id` | `thinq-open-{uuid}`, gerado uma vez e salvo na configuração |
| `x-api-key` | `v6GFvkweNo7DK7yD3ylIZ9w52aKBU0eJ7wLXkSR3` (chave pública do SDK) |
| `x-service-phase` | `OP` |

O `POST` de controle leva ainda `x-conditional-control: true`.

**Endpoints usados:**

| Método | Caminho | Uso |
|---|---|---|
| `GET` | `devices` | listar aparelhos da conta |
| `GET` | `devices/{id}/profile` | capacidades do aparelho |
| `GET` | `devices/{id}/state` | estado atual |
| `POST` | `devices/{id}/control` | enviar comando |

**Resposta:** em sucesso, o conteúdo útil está na chave `response`. Em erro, o
corpo traz `error.code` e `error.message`.

**Payloads de controle** (`{recurso: {propriedade: valor}}`):

| Ação | Payload |
|---|---|
| Ligar / desligar | `{"operation": {"airConOperationMode": "POWER_ON" \| "POWER_OFF"}}` |
| Modo | `{"airConJobMode": {"currentJobMode": "COOL"}}` |
| Temperatura | `{"temperatureInUnits": {"targetTemperature": 23, "unit": "C"}}` |
| Cancelar timer | `{"timer": {"relativeStopTimer": "UNSET"}}` |
| Ventilação | `{"airFlow": {"windStrength": "HIGH"}}` |
| Oscilação vertical | `{"windDirection": {"rotateUpDown": true}}` |
| Oscilação horizontal | `{"windDirection": {"rotateLeftRight": true}}` |
| Economia | `{"powerSave": {"powerSaveEnabled": true}}` |
| Timer de desligamento | `{"timer": {"relativeHourToStop": 1, "relativeMinuteToStop": 30}}` |

Os valores aceitos em cada propriedade (lista de modos, velocidades, faixa de
temperatura) não são fixos no código: vêm do perfil do aparelho.

Pontos a confirmar com o aparelho real na primeira etapa, porque variam por
modelo:

- se a temperatura usa o recurso `temperatureInUnits` ou o `temperature`
  antigo;
- se a ventilação usa `windStrength` ou `windStrengthDetail`;
- o formato exato do payload de temperatura e o de cancelar o timer.

`AcProfileParser` decide entre essas variantes olhando o que existe no perfil,
e `AcCommands` usa a variante escolhida.

## Painel

Janela WPF sem borda, cerca de 360 px de largura, altura conforme o conteúdo.
Fundo translúcido (Mica/Acrylic) no Windows 11, com cor sólida como alternativa
no Windows 10. Cantos arredondados. Abre com animação curta de deslizar e
aparecer, ancorada ao canto da área de trabalho onde está a bandeja (leva em
conta a posição da barra de tarefas e a escala de DPI do monitor). Fecha ao
perder o foco ou com Esc.

De cima para baixo:

1. **Cabeçalho** — nome do aparelho, temperatura ambiente, botão grande de
   ligar/desligar.
2. **Temperatura** — mostrador circular com a temperatura alvo em destaque e
   botões − / +. O arco e a cor de destaque acompanham o modo (azul no frio,
   laranja no aquecer, verde-água no desumidificar, cinza no ventilar).
3. **Modo** — fileira de botões com ícone e rótulo.
4. **Ventilação** — seletor segmentado de velocidade.
5. **Extras** — interruptores de oscilação vertical, oscilação horizontal e
   economia; seletor de timer de desligamento (desativado, 30 min, 1 h, 2 h,
   4 h) com o tempo restante quando ativo.

Regras:

- Só aparece o controle que o perfil do aparelho declara como suportado e
  gravável.
- Com o ar desligado, tudo abaixo do cabeçalho fica esmaecido e desabilitado.
- No modo ventilar, o mostrador de temperatura fica desabilitado.
- Tema claro ou escuro segue o Windows e troca sem reiniciar.
- Textos em inglês (decisão de 2026-10-11, depois da primeira versão).

### Ícone da bandeja

- Ligado: a temperatura alvo desenhada no ícone, na cor do modo.
- Desligado: ícone acinzentado.
- Sem conexão ou com erro: ícone com marca de alerta.
- Dica ao passar o mouse: nome, estado, temperatura ambiente e alvo.
- Clique esquerdo: abre ou fecha o painel.
- Clique direito: menu com Ligar/Desligar, Iniciar com o Windows,
  Configurações e Sair.

## Fluxo de dados

**Leitura.** `AcController` busca o estado ao iniciar, ao abrir o painel, a
cada 30 s com o painel aberto e a cada 5 min com ele fechado. O perfil é lido
uma vez na inicialização.

**Comando.**

1. A UI chama o controller (ex.: `SetTargetTemperature(23)`).
2. O controller atualiza `AcState` imediatamente e dispara `StateChanged`.
3. O envio é adiado 600 ms por tipo de comando; uma nova chamada do mesmo tipo
   nesse intervalo substitui a anterior, de modo que só o valor final vai para
   a LG.
4. Em sucesso, o controller relê o estado 2 s depois para confirmar.
5. Em falha, o controller restaura o valor anterior, dispara `StateChanged` e
   `ErrorOccurred`.

Enquanto há um comando pendente ou em envio, o resultado do polling não
sobrescreve o campo afetado.

**Threads.** Todas as chamadas de rede são assíncronas. `AcController`
publica os eventos no dispatcher da UI.

## Configuração

Arquivo `%APPDATA%\LgController\settings.json`:

```json
{
  "token": "<base64 do token protegido por DPAPI>",
  "countryCode": "BR",
  "clientId": "thinq-open-…",
  "deviceId": "…",
  "deviceAlias": "Ar do quarto"
}
```

O token é protegido com `ProtectedData.Protect` no escopo `CurrentUser`.

**Primeira execução** (ou quando falta configuração válida): `SetupWindow`
pede o token, com um link para `https://connect-pat.lgthinq.com`, e o país
(padrão `BR`). O botão Conectar lista os aparelhos do tipo
`DEVICE_AIR_CONDITIONER`; o usuário escolhe um e salva. Se houver só um, ele já
vem selecionado.

**Iniciar com o Windows:** valor em
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, ligado e desligado pelo
menu da bandeja.

**Instância única:** um mutex nomeado; uma segunda execução apenas encerra.

## Erros

| Situação | Como é detectada | Comportamento |
|---|---|---|
| Sem internet | `HttpRequestException` / timeout | Faixa "Sem conexão" no painel, ícone de alerta, nova tentativa no próximo polling |
| Token inválido ou expirado | HTTP 401 ou código de token da API | Mensagem e botão que abre a configuração |
| Aparelho offline | Código de erro de aparelho da API | Faixa "Aparelho offline", controles desabilitados |
| Limite de chamadas | HTTP 429 ou código de limite da API | Faixa "Muitas requisições", polling pausado por 1 min |
| Comando rejeitado | Erro no `POST` de controle | Valor volta ao anterior, aviso temporário no painel |
| Outro | Qualquer `ThinQException` restante | Aviso com a mensagem da API |

`ThinQErrorKind` tem os valores `Network`, `Unauthorized`, `DeviceOffline`,
`RateLimited`, `CommandRejected` e `Unknown`. O mapeamento dos códigos
numéricos da API para esses valores é preenchido a partir da tabela de erros
do SDK oficial.

Exceções não tratadas são gravadas em `%APPDATA%\LgController\log.txt` e não
derrubam o aplicativo.

## Testes

Unitários, com xUnit:

- `ThinQClient` com `HttpMessageHandler` simulado: cabeçalhos, URL por região,
  extração de `response`, conversão de erros em `ThinQException`.
- `ThinQRegion`: país → região, país não suportado.
- `AcProfileParser`: perfis de exemplo → `AcCapabilities`, incluindo aparelho
  sem aquecimento e sem oscilação horizontal, e as variantes de recurso de
  temperatura e de ventilação.
- `AcStateParser`: estado de exemplo → `AcState`.
- `AcCommands`: cada ação → payload esperado.
- `AcController` com cliente simulado e relógio controlável: atualização
  otimista, agrupamento dos 600 ms, restauração em falha, polling que não
  sobrescreve comando pendente.
- `SettingsStore`: gravar e ler, token ilegível em disco.

Manuais:

- Etapa inicial, antes da UI: um executável de console lista os aparelhos,
  grava o perfil e o estado reais em arquivos (que viram os dados de exemplo
  dos testes), liga e desliga o ar.
- Ao final: percorrer todos os controles do painel com o aparelho real, em
  tema claro e escuro, e com a barra de tarefas embaixo.

## Ordem de construção

1. Solução, projetos e cliente `ThinQ/` com testes.
2. Verificação com o aparelho real e captura do perfil e do estado.
3. `Core/` (parsers, comandos, controller, configuração) com testes.
4. Bandeja e tela de configuração.
5. Painel: estrutura e ligação com o controller.
6. Painel: acabamento visual, animação, temas, ícone dinâmico.
7. Erros, iniciar com o Windows, instância única, publicação em `.exe`.
