# Neon Devourer — documento de design

Referência completa do jogo: o que ele é, todos os sistemas, as tabelas de
números e onde cada coisa vive no código. Escrito a partir do `index.html`
real, não de memória.

**Versão do jogo:** v2.0.0 · **Arquivo:** `index.html`, ~7.200 linhas,
autocontido (CSS e JS embutidos, sem dependência, sem build).

---

## 1. O conceito

Arcade de labirinto em vista de cima. Você é um **Devorador**: percorre uma
grade gerada do zero comendo **núcleos** de energia. Limpar todos conclui a
fase.

**A mecânica assinatura** é o que o nome diz. Espalhados pelo mapa há
**super-núcleos**; comer um liga o modo **DEVORAR** por alguns segundos, e
nessa janela os inimigos ficam assustados e podem ser devorados. Cada espécie
devorada solta uma **essência** diferente — um poder temporário. O jogo é sobre
inverter a caça.

**A ficção**, que amarra na mecânica: a grade é um cemitério de devoradores.
Cada chefe é um que veio antes, comeu além da própria fase e ficou preso nela.
Passar pelo último não é vencer a fome — é virar a próxima lápide, ou aprender
a parar antes.

---

## 2. Os sistemas, um por um

### 2.1 Recurso: Carga (0–100)

Enche comendo: núcleo comum **+2**, super-núcleo **+12** (dobra com Prisma).
Gasta em Pulso, Fase e Estase. Não tem teto de tempo — é o único recurso da
partida.

### 2.2 Habilidades ativas

| Habilidade | Tecla | Custo | Recarga | Destrava | O que faz |
|---|---|---|---|---|---|
| **Dash** | `ESPAÇO` | — | `4,5 × (1 − recarga×0,12)`s | início | avança vários tiles; atravessa mina e laser |
| **Pulso** | `SHIFT` | `40 × (1 − pulso×0,06)` | 1,2s | nível 3 | atordoa mobs no raio `3,2 + pulso×0,4` |
| **Fase** | `E` | 60 | 10s (6s com Sobrecarga) | nível 6 | atravessa paredes por 2,2s |
| **Estase** | `Q` | 50 | 14s | **chefe do cap. 5** | congela o ritmo do bioma num raio por 3,2s |

**Por que a Estase existe:** Dash resolve posição, Pulso resolve *estrutura*
(mob, rachadura, mina), Fase resolve escape. Faltava um verbo para o **fluxo** —
esteira, laser, decaimento da purga, escuridão e gelo são o cenário se movendo,
e nada respondia a isso.

O Pulso ganha papel extra em dois biomas: na FRATURA adia as rachaduras do raio,
no ESTOPIM detona minas à distância.

### 2.3 As 6 espécies de mob

| id | Nome | Cor | Vel. | XP | Comportamento | Essência que solta |
|---|---|---|---|---|---|---|
| `cacador` | Caçador | `#ff3b6b` | 1,00 | 25 | persegue direto | FÚRIA |
| `emboscador` | Emboscador | `#ff8ad4` | 1,02 | 30 | corta o seu caminho | IMPULSO |
| `errante` | Errante | `#39e0ff` | 0,95 | 20 | vagueia sem lógica | PRISMA |
| `guardiao` | Guardião | `#ffa63d` | 0,98 | 28 | recua se você chega perto | CASCO |
| `divisor` | Divisor | `#3dff9e` | 0,88 | 35 | racha em dois ao morrer | VÓRTICE |
| `sentinela` | Sentinela | `#b46bff` | 0,75 | 40 | atira lentidão | SOBRECARGA |

### 2.4 As 6 essências (buffs temporários)

| Essência | Ícone | Duração | Efeito |
|---|---|---|---|
| FÚRIA | ⚡ | 6s | +35% de velocidade |
| IMPULSO | » | 7s | dash sem espera e mais longo |
| PRISMA | ✦ | 8s | núcleos valem o dobro |
| CASCO | ✜ | 9s | anula o próximo dano |
| VÓRTICE | ◉ | 8s | atrai núcleos de longe |
| SOBRECARGA | ※ | 7s | habilidades sem custo |

### 2.5 Os 10 biomas

Cada capítulo **é** um bioma. Trocar paleta não seria bioma novo — cada um traz
uma regra que nenhum outro tem.

| # | Bioma | Cor | Layout | `mod` | Regra |
|---|---|---|---|---|---|
| 1 | CIRCUITO | `#39e0ff` | dfs | — | labirinto denso |
| 2 | COLMEIA | `#3dff9e` | cavernas | — | câmaras abertas, mais mobs |
| 3 | CRIOGENIA | `#8ad4ff` | dfs | `gelo` | você derrapa nas curvas (+18% de velocidade) |
| 4 | FISSURA | `#ff3bd0` | arena | `portais` | pares de portais instáveis |
| 5 | BLECAUTE | `#7b5cff` | dfs | `escuro` | só enxerga o que está perto |
| 6 | CORRENTE | `#ffd23f` | cavernas | `esteiras` | o piso empurra quem pisa |
| 7 | FRATURA | `#aab4c2` | cavernas | `colapso` | o piso racha e vira parede |
| 8 | ESTOPIM | `#c6ff3b` | arena | `minas` | minas visíveis explodem ao toque |
| 9 | FORNALHA | `#ff5d3b` | arena | `lasers` | feixes varrem os corredores |
| 10 | PURGA | `#ff3b6b` | dfs | `purga` | os núcleos se apagam sozinhos |

O BLECAUTE tem `corHud` própria (`#8f74ff`): o roxo do mapa dava 4,20:1 no
rótulo, abaixo de AA. Só o rótulo clareia.

**FRATURA em detalhe.** O piso racha com aviso visível e a célula que rompe vira
parede pelo resto da fase. Nenhum tile cai sem passar pelo mesmo teste de
conectividade do gerador de labirinto: simula a remoção, roda o campo de
distâncias a partir do spawn, confirma que nada ficou inalcançável, e desfaz se
reprovar. Teto de 20% do chão. Como nunca colapsa tile com núcleo, **o chão cede
atrás de quem já passou**.

**ESTOPIM em detalhe.** Minas não mexem no grid, então a fase nunca fica
estruturalmente impossível. O cuidado é contra dano garantido: no máximo **2
minas em passagem obrigatória**, nunca a menos de 3 tiles do spawn nem uma da
outra, e mina detonada vira chão seguro.

---

## 3. Campanha

**10 capítulos × 10 fases = 100 fases.** `CAPITULOS` deriva de `BIOMAS` — crescer
o array de biomas cresce a campanha sozinho. Da fase **101** em diante é o **Modo
Infinito**, que é o jogo original intacto, sorteando entre os 10 biomas.

### Ritmo do capítulo

| Fase | Papel | Intensidade (caçada = 100) |
|---|---|---|
| 1 | abertura | 55 |
| 2 | mod em ação | 70 |
| 3 | caçada | 100 |
| 4 | **descanso** | 45 |
| 5 | **mini-chefe** | prova cronometrada |
| 6 | mod reforçado | 115 |
| 7 | caçada dura | 130 |
| 8 | **descanso** | 50 |
| 9 | ensaio do chefe | 150 |
| 10 | **chefe** | estrutura própria |

A fase 9 é o **chefe disfarçado de fase**: o objetivo dela ensaia o padrão da 10.

### As curvas

As três curvas da campanha são dirigidas pelo **capítulo**, não pela fase
absoluta — e **colapsam algebricamente nas antigas no capítulo 1**, com
diferença medida de exatamente zero fase a fase.

```js
agressaoCampanha(f) = clamp( (cap-1)/9 × 0,9 + max(0, sub-8)/20 , 0, 1 )
escalaCampanha(f)   = 4,5 + min(10, sub)×0,13 + max(0, cap-1)×0,20
bonusMoedaDaFase(f) = 40 + sub×15 + max(0, cap-1)×22
```

| Capítulo | Agressão (abre → chefe) | Escala (abre → chefe) |
|---|---|---|
| 1 | 0,00 → 0,10 | 4,63 → 5,80 |
| 5 | 0,40 → 0,50 | 5,43 → 6,60 |
| 10 | 0,90 → 1,00 | 6,43 → 7,60 |

A agressão só chega em 1,0 na fase 100, a última.

**Por que existem:** as fórmulas antigas saturavam na fase 28 e 30. Em 100 fases
isso significaria sete capítulos com a mesma base. E a economia era pior: `40 +
fase×15` somado de 1 a 100 dá **79.750** créditos contra um catálogo de 16.750 —
476% de tudo que existe pra comprar. A fórmula nova dá 22.150, ou **1,32×**.

### Repescagem

Refazer fase vencida, pelo Mapa da Campanha. Paga **60% → 40% → 22%** ao repetir
a mesma fase; o contador zera ao vencer uma fase nova. **Não dá** XP, progresso
de capítulo, fragmento (nem em fase de chefe), ranking nem conquista. Os núcleos
comidos durante a partida valem o normal — só o bônus de conclusão decai, pra
nada parecer nerfado enquanto se joga.

Não precisou de penalidade artificial pra não virar a rota ótima: o bônus cresce
com capítulo e sub-fase, então a próxima fase sempre vale mais cheia do que a
melhor já vencida vale decaída.

---

## 4. Os 10 chefes

Fecham as fases 10, 20 … 100. **Você não atira neles** — o chefe fica no ninho e
alterna entre **blindado** (intocável, guardiões saem) e **aberto** (nascem
núcleos-chefe em estrela ao redor; comer cada um é uma mordida). Entre os dois
vem o **aviso**.

**A regra de escalada:** o chefe do capítulo N só usa como estágio um `mod` que o
jogador já atravessou. Por isso o primeiro tem um estágio e geometria pura, e o
último tem três com o repertório inteiro — a luta final é a prova do que os
capítulos ensinaram.

| Cap | Chefe | HP | Ciclo | Aviso | Guardiões | Estágios | Quem foi |
|---|---|---|---|---|---|---|---|
| 1 | **FAMINTO** | 3 | 11+5 | 1,20s | 1 / 16s | — | o mais recente antes de você; ainda quase tem sua cara |
| 2 | **PROLÍFERA** | 4 | 11+4 | 1,14s | 2 / 14s | — | uma boca parou de bastar, virou muitas |
| 3 | **GLACIAL** | 5 | 10+5 | 1,08s | 1 / 12s | `gelo` | o frio fechou no meio da última mordida |
| 4 | **RASGO** | 6 | 10+4 | 1,02s | 2 / 11s | `gelo`, `portais` | abria portais pra comer mais rápido do que digeria |
| 5 | **FUSÍVEL** | 7 | 9+5 | 0,96s | 2 / 10s | `portais`, `escuro` | devorou tanta energia que apagou o próprio circuito |
| 6 | **CORRENTEZA** | 9 | 9+4 | 0,90s | 2 / 8s | `gelo`, `escuro`, `esteiras` | nadou contra a esteira até a corrente aprender seu ritmo |
| 7 | **SUMIDOURO** | 10 | 9+4 | 0,85s | 2 / 7s | `esteiras`, `escuro`, `colapso` | cavou tão fundo que o chão parou de aguentar |
| 8 | **FAGULHA** | 11 | 8+4 | 0,80s | 2 / 6,5s | `colapso`, `portais`, `minas` | comeu rápido demais e o calor não teve pra onde ir |
| 9 | **INCANDESCENTE** | 12 | 8+4 | 0,75s | 3 / 6s | `minas`, `escuro`, `lasers` | parou de notar diferença entre a mordida e o laser |
| 10 | **VORAZ** | 15 | 7+4 | **0,70s** | 3 / 5,5s | `purga` + 2 sorteados | o primeiro de todos; devorou o próprio ninho |

**A mordida custa sempre 1 vida**, do primeiro ao último. O que escala é a
dificuldade de **ler a janela** — 1,20s a 0,70s, −42%.

**O Voraz** é o único com estágios sorteados entre os 7 mods do jogo, então muda
de partida pra partida. E ao trocar de estágio ele **devora o próprio ninho**: a
arena encolhe de verdade, com você dentro.

**Coop:** HP × (1 + 0,35 × jogadores extras); intervalo de guardião ×0,88 por
jogador, com **piso de 4s** — sem ele, o capítulo 10 com 4 jogadores geraria
guardião a cada 3,75s e viraria spam ilegível. **O aviso da mordida nunca encolhe
por causa de coop:** acessibilidade não se paga com dificuldade de leitura.

---

## 5. Os 10 mini-chefes

Na fase 5 de cada capítulo. São **mecanismos, não criaturas** — não trocam dano,
armam uma prova cronometrada e saem. Essa é a diferença deliberada em relação aos
chefes: senão o capítulo teria o mesmo combate duas vezes.

| Cap | Bioma | Mini-chefe | Tipo | Alvos | Janela |
|---|---|---|---|---|---|
| 1 | Circuito | O ARAUTO | sequência | 4 | 12s |
| 2 | Colmeia | O ALARME | coleta | 5 | 11s |
| 3 | Criogenia | O CRISTAL-ECO | sequência | 4 | 11s |
| 4 | Fissura | O DUPLO | sequência | 4 | 10s |
| 5 | Blecaute | O OLHO | coleta | 4 | 10s |
| 6 | Corrente | O REGENTE | sequência | 5 | 10s |
| 7 | Fratura | O ANTECIPADOR | coleta | 5 | 9s |
| 8 | Estopim | O DETONADOR | sequência | 4 | 9s |
| 9 | Fornalha | O METRÔNOMO | sequência | 4 | 8s |
| 10 | Purga | O ÚLTIMO SINAL | coleta | 6 | **7s** |

**Decisão de projeto:** em vez de dez sistemas separados pela metade, o motor tem
**dois verbos** — seguir alvos em ordem, ou colher alvos no tempo — e quem
diferencia os dez é o **bioma**. O gelo faz a sequência escorregar, o escuro
esconde os alvos, a esteira empurra pra fora da rota, o piso racha embaixo deles,
o laser corta o caminho.

**Falhar custa 1 vida e fecha o portal. Nunca mata** — nenhuma fase pode ser
bloqueada por objetivo extra; limpar os núcleos continua bastando sozinho.

O alvo é um **hexágono de contorno branco** com o número da ordem escrito dentro.
Branco porque precisa ser visível no Blecaute; número em vez de cor porque ordem
por tom exclui quem não separa matiz. A forma é inédita de propósito — núcleo é
círculo, super é anel, mina é losango, núcleo-chefe é estrela, alvo é hexágono.

---

## 6. A dimensão fora da grade

Cumprir o desafio do mini-chefe abre um **portal**, que fica 15s. Ignorá-lo não
custa nada — é prêmio, não caminho.

Atravessar leva a outra física: **inércia, rotação e tela aberta com wrap nas
quatro bordas**. No labirinto a direção é um estado; ali é uma força.

| | Labirinto | Dimensão |
|---|---|---|
| Movimento | 4 direções, colisão por tile | inércia e empuxo, colisão por raio |
| Controle | direção é estado | girar e empurrar, teclas seguradas |
| Borda | parede | wrap nas 4 |
| Dash | avança tiles | propulsão |
| Pulso | atordoa | onda que empurra os cacos |
| Fase | atravessa parede | atravessa caco |
| **Exclusivo** | — | **tiro que ricocheteia na borda** |

Uma única habilidade é exclusiva daqui, porque **uma nova é lembrada e quatro
viram confusão**. O ricochete só faz sentido onde não existe parede.

A **carga vira combustível**, o que dá motivo pra abrir o portal cheio. Os cacos
se partem em dois ao serem atingidos. O núcleo tem `6 + capítulo×2` de vida e
cospe mais cacos enquanto vive. Bater num caco encerra a dimensão, não a partida.

**A fase do labirinto fica congelada** enquanto a dimensão está aberta — o laço
principal desvia inteiro, nada ali toca o grid, e ao voltar fase, bioma e núcleos
restantes estão exatamente como estavam.

---

## 7. Progressão

### Tier 1 — Talentos (moeda: pontos de nível)

`xpNecessario(nv) = 100 + nv×70`. Um ponto por nível.

| id | Nome | Máx | Req | Efeito por nível |
|---|---|---|---|---|
| `veloc` | Velocidade | 5 | 1 | +4% de deslocamento |
| `recarga` | Recarga | 5 | 1 | −12% no tempo do Dash |
| `devorar` | Devorar | 5 | 1 | +0,8s de modo devorar |
| `ima` | Ímã | 3 | 2 | atrai núcleos a 0,9 tiles |
| `pulso` | Pulso | 5 | 3 | +0,4 de raio, −6% de custo |
| `escudo` | Escudo | 3 | 4 | 1 barreira, recarga 20s |

Teto somado: **26 pontos**. Com 100 fases de XP, todo mundo enche antes do
capítulo 5 — foi por isso que o tier 2 existe.

### Tier 2 — Núcleo Instável (moeda: Fragmento de Chefe)

**1 fragmento por chefe, no primeiro abate.** São 10 numa campanha inteira e os
6 talentos custam **16** — ninguém compra tudo. Vira escolha de build, não lista
de compras. Destrava ao vencer o chefe do capítulo 5.

| id | Nome | Custo | Bioma | Efeito | **Contrapartida** |
|---|---|---|---|---|---|
| `garra` | BOTAS DE GARRA | 2◈ | Criogenia | sem derrapagem no gelo | −6% de velocidade fora dela |
| `contrafluxo` | CONTRAFLUXO | 3◈ | Corrente | esteiras empurram mobs; a favor você acelera | contra a esteira empurra mais forte |
| `pararraios` | PARA-RAIOS | 2◈ | Fornalha | parado no aviso, absorve o feixe e vira carga | ficar parado é janela pro mob |
| `colheita` | COLHEITA AMARGA | 3◈ | Purga | núcleo quase apagando vale 3× | o seguinte vale metade |
| `ondadechoque` | ONDA DE CHOQUE | 4◈ | Estopim | rachadura e mina atordoam num raio maior | o primeiro estouro perto ignora o Escudo |
| `visaonoturna` | OLHOS NA ESCURIDÃO | 2◈ | Blecaute | enxerga mais longe, núcleos brilham | os mobs também te enxergam de mais longe |

Passiva sem contrapartida é só "+10%", e isso não é profundidade.

---

## 8. Loja

Moeda: **Créditos**, ganhos jogando. Catálogo calibrado em 16.750; skins ocupam
7.450, melhorias e itens cabem nos ~9.300 restantes.

**Skins** (cosméticas, nunca dão vantagem): Plasma 0 · Serra 150 · Orbe 300 ·
Reator 550 · Cometa 850 · Cristal 1200 · Aracno 1800 · Monarca 2600.

**Melhorias** (permanentes, valem em toda fase e todo modo):

| id | Nome | Custo | Efeito |
|---|---|---|---|
| `farol` | FAROL | 900 | o núcleo mais próximo aparece marcado no HUD |
| `cofre` | COFRE | 500/1200/2200 | +8/16/25% de crédito por fase |
| `bolso` | BOLSO FUNDO | 1800 | +1 vida máxima, para sempre |
| `memoria` | BOA MEMÓRIA | 2200 | metade dos núcleos continua colhida ao perder vida |

**Itens** (estoque, gastam, consumidos sozinhos ao entrar na fase):

| id | Nome | Custo | Máx | Efeito |
|---|---|---|---|---|
| `folego` | FÔLEGO | 150 | 5 | começa a fase com a carga cheia |
| `casco` | CASCO DE RESERVA | 350 | 3 | entra com um Casco ativo |
| `revive` | NÚCLEO DE EMERGÊNCIA | 450 | 3 | devolve a vida quando ela zeraria, 1× por fase |

Nenhum dá poder de combate bruto — são segurança, informação e economia. Quem
carrega a progressão são os talentos, e a loja não pode competir com eles.

---

## 9. Conquistas

Dez, em três camadas: marcos da campanha, maestria dos biomas com mecânica
própria, e coleção. Pagam crédito, então alimentam a loja sem depender de
repescagem.

Primeira Mordida (80¢) · Meio do Cemitério (250¢) · Fim da Fome (800¢) · Não Era
Pra Tanto (400¢) · Atravessa Rachadura (200¢) · Pavio Curto (200¢) · Devorador
Nato (200¢) · Guarda-Roupa Completo (300¢) · Núcleo Instável (250¢) · A Fome Não
Acaba (150¢).

---

## 10. Multiplayer

**Local:** dupla no mesmo teclado (WASD + setas), coop ou versus.

**Online:** sala por código, host autoritativo em Phoenix **feito na mão, sem
SDK**. Cada broadcast tem um `phx_reply` que precisa ser tratado — é o ponto
historicamente frágil deste jogo.

FRATURA e ESTOPIM foram os primeiros mods a mudar o mapa **durante** a fase, e
`serializarMapa()` só manda grid e pellets uma vez. Sem tratamento o convidado
divergiria — um enxergando parede onde o outro tem chão. O pacote leva as minas
iniciais e o que muda depois vai por evento autoritativo (`racha`, `colapso`,
`detona`), com o convidado só reproduzindo.

**Não foi validado com duas pessoas de verdade.** É o risco conhecido.

---

## 11. Telas

Menu · Mapa da Campanha · Talentos (abas TALENTOS/NÚCLEOS) · Loja (abas
SKINS/MELHORIAS/ITENS) · Conquistas · Como Jogar · Ajustes · Perfil ·
Multiplayer (dupla, online, sala, espera) · Pausa · Fim de Jogo · Ranking.

O **manual se escreve sozinho** a partir das tabelas do jogo — a seção de biomas
percorre `BIOMAS`, então bioma novo entra sem ninguém editar o texto.

---

## 12. Desempenho

O gargalo é o `shadowBlur` (93 ocorrências) — o brilho neon. `QUALIDADE`
multiplica todos eles; zerar rende **+75% de quadro**, medido.

**Qualidade adaptativa:** o jogo mede o quadro enquanto roda e, se ficar abaixo
de 45 fps por três janelas seguidas, abre mão do brilho sozinho — **e avisa**,
com o caminho de volta escrito. Quem religa nos Ajustes assume a escolha e o
automático não mexe mais. Mexer na qualidade escondido seria o mesmo padrão
escuro que a doutrina proíbe na loja.

| Cenário, CPU 4× | Antes | Depois |
|---|---|---|
| Chefe final | 18,0 fps | **37,5 fps** |
| Capítulo 9, 12 mobs | — | 38,0 fps |

---

## 13. Save

Chave `neonDevourer_v1`. `carregarSave()` faz `Object.assign(saveBase(), s)`,
então **campo novo é preenchido sozinho** para quem já joga — foi assim que
campanha, loja e conquistas entraram sem quebrar save existente.

Campos: `charLevel · xp · pontos · coins · skin · skins[] · cores{} · up{} ·
up2{} · fragmentos · recorde · faseMax · campanhaMax · campanhaFim · chefes[] ·
conquistas[] · melhorias{} · itens{} · estat{} · repescagem{} · som · tremor ·
brilho · brilhoManual · ranking[] · nome · pendentes[]`.

> **Cuidado histórico.** O jogo passou meses descartando todo save em silêncio:
> `carregarSave()` roda na linha 801 e chama `consolidarRanking()`, que lê
> `RANK_GUARDA` — uma `const` declarada mais abaixo. `const` fica em zona morta
> temporal até a própria linha rodar, o acesso estourava `ReferenceError`, e o
> `catch` engolia calado. As três declarações moram acima da seção SAVE por esse
> motivo. **Mover de volta traz o bug.**

---

## 14. Regras que não se negociam

1. Um `index.html` único e autocontido, funcionando sob `file://`.
2. Modo vitrine `?attract=1`: bot joga, `setItem` em no-op, tamanho por
   `postMessage`. O ranking global **não** é buscado ali.
3. Mordida, mina, queda e laser custam **1 vida** — a dificuldade está na
   leitura, nunca no dano.
4. Nenhuma fase pode ser bloqueada por objetivo extra.
5. Perigo e prêmio se distinguem por **forma**, não só por cor.
6. Estado nunca é só cor: sempre com ícone ou rótulo escrito.
7. Nada escondido do jogador — nem economia, nem dificuldade, nem qualidade.

---

## 15. O que ainda não foi feito

- **Multiplayer online nunca validado** com duas pessoas reais.
- **Toque não testado em aparelho de verdade** — só emulado.
- **WebGL**: resolveria o custo de render de goleada, mas só depois de espremer
  o canvas 2D.
- **Play Store**: exige Capacitor, política de privacidade, e RLS no Supabase
  (a chave anônima viajaria dentro do APK).
