# Showcase

Um repositório único com minijogos e ferramentas práticas — cada um roda direto no navegador, sem instalação, sem dependências e sem servidor. Feito pra ser jogado/testado com um clique via GitHub Pages.

Cada projeto vive na sua própria pasta e é 100% autocontido (`index.html` único com CSS e JS embutidos).

## 🕹️ Portfólio ao vivo

O [`index.html` da raiz](index.html) é a capa do showcase: os minijogos organizados por categoria, e **cada capa é o jogo de verdade rodando sozinho** — não é print nem vídeo. Passe o mouse e aparece o botão de jogar.

> **[▶ Abrir o portfólio](https://jvaz-deveng.github.io/showcase/)**

Como funciona: cada jogo aceita `?attract=1` e entra em **modo vitrine** — um piloto automático joga sozinho em loop, o título e o rodapé somem para o palco ocupar o card, e `Storage.prototype.setItem` vira no-op para o bot nunca gravar por cima do recorde de quem joga. A página carrega os jogos em `<iframe>` reduzido por `transform:scale`, mas só mantém **3 rodando ao mesmo tempo** (os mais próximos do centro da tela, via `IntersectionObserver`), desliga os que saem de vista, pausa tudo quando a aba fica escondida e respeita `prefers-reduced-motion` mostrando só o `poster.png`. O enquadramento não é chutado: cada vitrine informa o próprio tamanho ao portfólio por `postMessage`, porque sob `file://` o iframe é origem opaca e a página não consegue medi-lo.

Ao adicionar um jogo novo, dê a ele o bloco `?attract=1`, gere o `poster.png` e acrescente o card no `index.html` da raiz.

## 🎮 Minijogos

| Projeto | Descrição | Jogar |
|---|---|---|
| [Reflex Rush](minijogos/reflex-rush/) | Jogo de reflexo: acerte alvos que somem rápido, monte combos, 30 segundos por rodada, recorde salvo localmente. | [Demo ao vivo](https://jvaz-deveng.github.io/showcase/minijogos/reflex-rush/) |
| [Fusion Rush](minijogos/fusion-rush/) | Física de merge (bolhas iguais se fundem numa maior) com combo por velocidade, modo sem cronômetro (só acaba se o pote transbordar) e um gato ativo tentando roubar bolhas pelas laterais — acerta e você ganha uma bola de força que sacode a pilha, erra 5 vezes seguidas e ele se estressa e estoura sua maior bolha. Desafio diário compartilhável estilo Wordle. | [Demo ao vivo](https://jvaz-deveng.github.io/showcase/minijogos/fusion-rush/) |
| [Mata Barata](minijogos/mata-barata/) | Whack-a-mole: esmague as baratas antes que fujam, monte combo (reseta se uma escapar), dourada vale 5x mais. Dificuldade sobe conforme você acerta. 30 segundos por rodada. | [Demo ao vivo](https://jvaz-deveng.github.io/showcase/minijogos/mata-barata/) |
| [Bubble Crane](minijogos/bubble-crane/) | Puzzle de ordenação em que **você opera o guindaste**: leve a garra até um bloco, pegue e empurre pra esquerda ou pra direita, trocando de lugar com o vizinho, até a esteira ficar em ordem crescente. O bloco na garra fica suspenso e passa por cima enquanto o vizinho desliza por baixo. Cada nível vem com um **orçamento de trocas** (o mínimo necessário + uma folga que encolhe a cada nível), e o placar **Desordem** mostra ao vivo quantas trocas ainda faltam no mínimo — sobe quando você empurra pro lado errado. Bloco no lugar certo trava em verde; fechar no número mínimo de trocas dá bônus. 3 vidas, arrays de 5 a 10 blocos, pontuação por trocas economizadas e por tempo, recorde salvo localmente, e um modo demonstração que roda o Bubble Sort sozinho até a passagem sem nenhuma troca. | [Demo ao vivo](https://jvaz-deveng.github.io/showcase/minijogos/bubble-crane/) |
| [Cosmic Crush](minijogos/cosmic-crush/) | Match-3 espacial completo, com loja, perfil, conquistas e tutorial. Combina **grupo conectado de 4+ peças em qualquer formato** (quadrado, L, T, S, cobrinha), levando o grupo inteiro. Campanha com **4 tipos de fase**: coletar peças, derreter todo o gelo, resgatar cápsulas até a base e bater uma pontuação. Combinação de 4 vira foguete, em L/T vira supernova e de 5 vira buraco negro — e **trocar duas especiais entre si** gera efeitos combinados (cruzeta, devastação de 3 linhas + 3 colunas, chuva de foguetes, colapso total). Bloqueadores de gelo (até 2 camadas), correntes que prendem a peça e matéria escura que se alastra a cada jogada. Sobrou movimento ao cumprir o objetivo? Vira **ignição final** e chove foguete. Mapa de fases com trilha, estrelas e recorde; 4 poderes ganhos ao concluir fases. Render otimizado com a peça pronta em cache (halo e arte assados num bitmap só) e fundo assado uma vez — 15 para 35 FPS num celular mediano medido com CPU estrangulada. Som sintetizado em Web Audio (sem arquivo, o jogo segue sendo um index.html único) com mudo persistido no botão da carteira e na tecla M. Animação completa: foguete que atravessa a tela deixando rastro, vórtice do buraco negro engolindo o tabuleiro, ondas da supernova, gelo estilhaçando, corrente arrebentando, martelo que desce e bate, cápsula saindo com propulsão, peças que amassam ao aterrissar e balançam paradas, tremor de tela proporcional ao estrago e telas que entram em cascata (com respeito a prefers-reduced-motion). Moeda de cristais ganha jogando, loja de poderes com pacotes, 10 conquistas, perfil com 10 estatísticas e tela de tutorial. 15 fases feitas à mão e fases infinitas geradas por semente, com dificuldade calibrada por simulação (sonda que joga cada fase 12× e mede o progresso). | [Demo ao vivo](https://jvaz-deveng.github.io/showcase/minijogos/cosmic-crush/) |

## Ferramentas

| Projeto | Descrição | Usar |
|---|---|---|
| [Gerador de Título SEO](ferramentas/gerador-titulo-seo/) | Monta títulos de anúncio otimizados para Mercado Livre, Amazon e Shopee a partir do produto e palavras-chave, respeitando limite de caracteres e boas práticas de cada marketplace. | [Demo ao vivo](https://jvaz-deveng.github.io/showcase/ferramentas/gerador-titulo-seo/) |
| [Assinador de MTR](ferramentas/assinador-mtr/) | Assina em lote o campo "assinatura do responsável" de Manifestos de Transporte de Resíduos (PDF) com fonte cursiva, posição travada e prévia ao vivo — 100% offline, nenhum arquivo sai do navegador (bibliotecas de PDF embutidas no próprio HTML). | [Demo ao vivo](https://jvaz-deveng.github.io/showcase/ferramentas/assinador-mtr/) |
| [Gerador de Relatório Fotográfico](ferramentas/gerador-relatorio-fotografico/) | Monta relatório fotográfico em PDF (A4, 6 fotos por página) a partir de um lote de imagens — legendas editáveis, modo antes/depois, cabeçalho e logotipo personalizáveis, ordenação automática por nome de arquivo. 100% no navegador. | [Demo ao vivo](https://jvaz-deveng.github.io/showcase/ferramentas/gerador-relatorio-fotografico/) |

## Identidade

Cada projeto assina o próprio rodapé com número de versão (semver) e link para [@jvaz-DEVENG](https://github.com/jvaz-DEVENG) — assim qualquer um que abrir um link isolado (sem passar pelo README) já reconhece de onde veio. Ao evoluir um projeto, suba a versão no rodapé (`vMAJOR.MINOR.PATCH`): PATCH para correção, MINOR para funcionalidade nova, MAJOR para mudança que quebra compatibilidade com versões salvas (ex: formato do `.json` do Gerador de Relatório Fotográfico).

Nas **ferramentas**, ícone de cabeçalho e favicon seguem um padrão único (linha fina, 24×24, cor sólida sobre fundo em gradiente) em vez de emoji coloridos de plataforma — reforça a cara de produto profissional. Os **minijogos** mantêm emoji na identidade visual, que combina mais com o tom descontraído deles.

## Stack

HTML + CSS + JavaScript puro. Sem frameworks, sem build step, sem `node_modules` — abrir o `index.html` já funciona, e o deploy é só `git push`.

## Rodando localmente

Basta abrir o `index.html` de qualquer projeto no navegador, ou servir a pasta com qualquer servidor estático:

```bash
python -m http.server 8000
```

## Licença

MIT — veja [LICENSE](LICENSE).