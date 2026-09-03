# APOLLO TANKS

Battle City online. Tudo que o NES tinha — tijolo que quebra, aço que resiste,
os seis bônus, a estrela que sobe o tanque, o gelo, a águia que não pode cair —
mais campanha de 5 fases, habilidade ativa por classe, upgrades entre as fases,
Modo Construção pra fortificar a águia, sala por código e até 4 jogadores.

```
JOGAR.bat          ← duplo clique e pronto
```

O navegador abre em `http://localhost:8080`. O console mostra também o endereço
com o IP da sua rede: manda esse pros amigos da mesma wi-fi.

---

## O ciclo de uma partida

```
menu → lobby → CONSTRUÇÃO → fase → INTERVALO → CONSTRUÇÃO → fase → … → fim
                    ↑                    ↑
              gasta a Sucata      escolhe 1 upgrade
              na defesa da        e recebe a Sucata
              águia               da fase que caiu
```

## Telas

| Tela | Pra que serve |
|---|---|
| **Menu** | criar ou entrar numa sala, e o seu recorde |
| **Perfil** | nome de piloto, skin do casco e a ficha de carreira (fica no seu navegador) |
| **Manual** | classes e habilidades, bônus, terreno e defesa da águia — tudo explicado |
| **Lobby** | quem está na sala, escolha de classe, campanha ou fase avulsa |
| **Modo Construção** | a obra: gasta Sucata em defesa da águia antes de cada fase |
| **Jogo** | o campo, o HUD e a pausa |
| **Intervalo** | placar por tipo de inimigo, a conta da Sucata e a escolha do upgrade |
| **Fim** | placar final e recorde |

## Como jogar com os amigos

**Na mesma casa/wi-fi:** rode o `JOGAR.bat`, pegue o endereço `http://192.168.x.x:8080`
que aparece no console e mande no grupo.

**De qualquer lugar, sem hospedar nada:** com o servidor rodando, abra outro
terminal e use um túnel do Cloudflare (não precisa de cadastro):

```
cloudflared tunnel --url http://localhost:8080
```

Ele devolve um endereço `https://algo.trycloudflare.com` que funciona pra
qualquer um. Enquanto o seu PC estiver ligado, a sala existe.

**Fluxo:** quem cria a sala recebe um código de 4 letras e um botão *Copiar link*.
Quem recebe o link já cai na tela com o código preenchido.

## Controles

| | |
|---|---|
| Mover | `WASD` ou setas |
| Atirar | `Espaço` (pode segurar) |
| Habilidade | `Shift` (ou `E`) |
| Pausar | `P` (só o host) |
| Celular | D-pad, botão FOGO e botão HAB |

## A campanha

Cinco fases em sequência. Vencer uma leva o esquadrão pra próxima com os pontos,
as vidas (mais uma de bônus) e os upgrades que já tinha.

| | Fase | O que muda |
|---|---|---|
| 1 | **Fortaleza** | o quintal de casa, muito espaço pra aprender |
| 2 | **Pântano** | água corta o caminho mas deixa o tiro passar; o mato esconde |
| 3 | **Usina** | caixas de aço que só o tiro perfurante abre |
| 4 | **Labirinto** | corredor apertado, pouca linha de tiro longa |
| 5 | **Cratera** | pátio aberto — e o **Colosso**, que aguenta 26 tiros e solta barragem pros 4 lados |

Cada fase entra com mais inimigos e com os tipos mais duros aparecendo com mais
frequência. Quem quiser treinar um mapa solto escolhe **Fase avulsa** no lobby.

**Entre as fases** cada jogador escolhe 1 de 3 upgrades — motor, gatilho leve,
pente extra, pólvora densa, blindagem, núcleo perfurante, reator frio ou tanque
reserva. Eles se acumulam até o fim da campanha.

## Sucata e o Modo Construção

**Sucata** (⛭) é a moeda do esquadrão — não é de cada um. Ela entra ao limpar
uma fase:

| | |
|---|---|
| Fase concluída | ⛭ 40 |
| Cada inimigo destruído | ⛭ 8 |
| Muro da águia intacto | ⛭ 60 |
| Ninguém do esquadrão caiu | ⛭ 40 |

E sai no **Modo Construção**, que abre antes de cada fase. Ali o esquadrão
inteiro compra defesa pra águia — quem comprar, comprou pra todo mundo. O que
já foi pago fica: as fortificações são montadas de novo no começo de cada fase.

| Fortificação | O que faz | Níveis |
|---|---|---|
| ▦ **Muralha de aço** | troca o tijolo do muro por aço, que só cai pra tiro perfurante | 1/3, 2/3, muro inteiro |
| ✚ **Blindagem da águia** | ela passa a aguentar tiro em vez de cair no primeiro | +1, +2, +3 tiros |
| ↻ **Auto-reparo** | o muro se refaz sozinho, sem precisar de um Suporte | a cada 8s, a cada 4s |
| ◈ **Campo de minas** | a primeira coisa inimiga que passa por cima vai pelos ares | 2, 4, 6 minas |
| ⌖ **Sentinela** | torre fixa que atira sozinha em quem entra na linha | 1, 2 torres |

A tela mostra a águia com tudo que já foi comprado, e a fase só começa quando
todo mundo aperta *Estou pronto* (ou quando o relógio de 45s vence).

## Os bônus do original

Um em cada cinco inimigos entra **piscando**. Quando ele cai, larga um bônus no
campo — e o bônus vale 500 pontos a mais pra quem pegar.

| | |
|---|---|
| ⛑ **Capacete** | escudo por 10 segundos |
| ⧗ **Relógio** | congela todos os inimigos por 8 segundos |
| ⛏ **Pá** | o muro da águia vira aço por 20 segundos (pisca nos últimos 3) |
| ★ **Estrela** | sobe um nível o seu tanque |
| ✹ **Granada** | explode todos os inimigos que estão em campo |
| ♥ **Tanque** | uma vida a mais |

A **estrela** funciona como no NES e some quando você morre: nível ★ dá tiro 35%
mais rápido, ★★ soma uma bala na tela, ★★★ faz o seu tiro rasgar aço.

Também como no original: cada tipo de inimigo vale o seu tanto (básico 100,
veloz 200, canhão 300, blindado 400, Colosso 2000), o placar de fim de fase
mostra a conta por tipo, e a cada 20 mil pontos você ganha uma vida.

## Classes e habilidades

| | Velocidade | Cadência | Tiro | Aguenta | Habilidade |
|---|---|---|---|---|---|
| ⚡ **Assalto** | alta | alta | comum, 2 na tela | 1 tiro | **Arranque** — dispara pra frente por meio segundo |
| 🛡 **Pesado** | baixa | baixa | perfura aço | 2 tiros | **Bastião** — escudo que aguenta tudo por 3 s |
| 🎯 **Sniper** | média | muito baixa | perfura aço, bem rápido | 1 tiro | **Perfurar** — o próximo tiro atravessa parede, aço e tanque sem parar |
| 🔧 **Suporte** | média-alta | média | comum, 2 na tela | 1 tiro | **Reparo** — refaz o muro da águia na hora e dá escudo a quem está perto |

O Suporte também reconstrói sozinho o muro em volta da águia quando fica por
perto, e volta ao jogo mais rápido depois de morrer.

## Regras da partida

- 3 vidas por jogador, mantidas de uma fase pra outra. Quem morre volta com 3
  segundos de escudo.
- No máximo 4 ou 5 inimigos em campo ao mesmo tempo, conforme a fase.
- **Você perde** se a águia cair ou se o esquadrão inteiro ficar sem vidas.
- **Você vence a campanha** derrubando o Colosso na fase 5.
- Igual ao original: dá pra destruir a própria águia sem querer. Cuidado com o
  tiro na horizontal perto da base.

Terreno: tijolo quebra com qualquer tiro · aço só cede a tiro perfurante ·
água bloqueia tanque mas deixa o tiro passar · mato esconde todo mundo ·
**gelo** deixa o tanque escorregar mais um pouco depois que você solta.

> No NES, quem virava em cima do gelo continuava deslizando na direção antiga.
> Aqui o deslize acompanha a direção nova: trava bem menos e continua sendo
> escorregadio.

---

## Como funciona por dentro

O servidor é a autoridade. Ninguém confia no cliente.

```
navegador                         servidor Node
  teclado ──► comando (20/s) ──►  simulação a 60 Hz  (shared/sim.js)
  previsão local do próprio           │
  tanque, a 60 fps                    ▼
  desenho ◄── retrato do mundo (20/s) ─┘
```

- **`shared/sim.js`** roda nos dois lados. O servidor simula tudo; o cliente
  reaproveita só o movimento pra prever o próprio tanque e não sentir o ping.
  Quando o retrato chega, a posição é corrigida devagar (ou teleportada, se a
  diferença for grande).
- **Os outros jogadores** são desenhados 110 ms no passado, interpolando entre
  dois retratos. É o que deixa o movimento liso mesmo com a rede irregular.
- **A habilidade** é um toque, não um estado: o cliente manda `hab` uma vez e o
  servidor decide se a recarga já venceu. Só o Arranque é adiantado localmente,
  pra não sentir o ping no que é justamente um movimento rápido.
- **A Sucata e as fortificações** vivem na sala, não no jogador: o servidor é
  quem debita e quem monta a defesa no começo da fase. O cliente só desenha.
  O perfil (nome, skin, recorde, ficha) é o contrário — fica só no navegador,
  no `localStorage`, e a skin é a única parte dele que viaja pela rede.
- **O mapa** vai inteiro uma vez e depois só em pedaços: cada tijolo que quebra
  vira um par `[célula, tipo]`.
- **O relógio:** o Windows só acorda temporizadores a cada ~15,6 ms, então um
  `setInterval(16,7 ms)` entregaria uns 36 ticks/s — o jogo rodaria a 60% da
  velocidade. O laço acorda mais vezes e roda quantos passos o tempo real pedir.

```
shared/    constants.js  maps.js  sim.js      ← regras, iguais nos dois lados
server/    index.js  room.js  ai.js           ← HTTP + WebSocket + salas + IA
client/    index.html  style.css  main.js  render.js  net.js
test/      smoke.js  net.js  browser.js
```

Nada de imagens: tanques, tijolos e a águia são desenhados por código num canvas
de 208×208 esticado por CSS — por isso o pixel grandão do NES. Cada classe e
cada tipo de inimigo tem silhueta própria (largura do casco, tamanho da torre,
comprimento do cano), e todas as medidas são inteiras: meio pixel num canvas
desse tamanho vira borrão.

## Testes

```
npm test              # regras do jogo (sem rede) + servidor real com 2 clientes
npm run test:browser  # abre 2 janelas headless e joga de verdade (precisa Edge ou Chrome)
```

Se o jogo parecer lento em alguma máquina, rode com `APOLLO_TICK_LOG=1` — o
servidor passa a informar a velocidade real da simulação.

O servidor é hostil por padrão a cliente mal-intencionado: nenhuma mensagem
derruba o processo (todo handler roda dentro de um `try`), o tamanho é limitado
a 64 KB e cada conexão só pode mandar 120 mensagens por segundo — o cliente
manda ~20. Vale lembrar que quem tem o endereço entra na sala; o jogo não tem
senha nem conta.

`APOLLO_INIMIGOS_POR_FASE=0` faz cada fase se limpar sozinha. Serve pro teste de
navegador conferir o intervalo entre fases sem ter que ganhar a partida.

## Próxima fase

A base já está preparada (campo `mode` na sala, times separados em `stepBullets`)
para os modos PvP: deathmatch, 2v2 e rei da base. O fogo amigo entre jogadores
já está desligado por time — quando entrar o PvP é só trocar a regra de time.

## Histórico anterior

Este projeto viveu como repositório próprio em `D:\APOLLO-CORE\apollo-tanks` até
ser absorvido pelo showcase em 2026-09-03. Os três commits daquele histórico,
para referência:

```
5378eaf  Foco visivel no teclado, menos movimento, e teste que fala com o navegador certo
4bc41cb  Separa amigo de inimigo por luminosidade, nao so por matiz
c2c7137  APOLLO TANKS: Battle City online com campanha e Modo Construcao
```

Ele **não** segue o contrato do showcase (`index.html` único, sem servidor):
tem servidor Node autoritativo e `package.json`, então roda por `JOGAR.bat`, não
pelo GitHub Pages. Por isso ainda não tem card no portfólio.
