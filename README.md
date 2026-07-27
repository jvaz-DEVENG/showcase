# 🚀 Showcase

Um repositório único com minijogos e ferramentas práticas — cada um roda direto no navegador, sem instalação, sem dependências e sem servidor. Feito pra ser jogado/testado com um clique via GitHub Pages.

Cada projeto vive na sua própria pasta e é 100% autocontido (`index.html` único com CSS e JS embutidos).

## 🎮 Minijogos

| Projeto | Descrição | Jogar |
|---|---|---|
| [Reflex Rush](minijogos/reflex-rush/) | Jogo de reflexo: acerte alvos que somem rápido, monte combos, 30 segundos por rodada, recorde salvo localmente. | [Demo ao vivo](#) |
| [Fusion Rush](minijogos/fusion-rush/) | Física de merge (bolhas iguais se fundem numa maior) com combo por velocidade, modo sem cronômetro (só acaba se o pote transbordar) e um gato ativo tentando roubar bolhas pelas laterais — acerta e você ganha uma bola de força que sacode a pilha, erra 5 vezes seguidas e ele se estressa e estoura sua maior bolha. Desafio diário compartilhável estilo Wordle. | [Demo ao vivo](#) |

## 🛠️ Ferramentas

| Projeto | Descrição | Usar |
|---|---|---|
| [Gerador de Título SEO](ferramentas/gerador-titulo-seo/) | Monta títulos de anúncio otimizados para Mercado Livre, Amazon e Shopee a partir do produto e palavras-chave, respeitando limite de caracteres e boas práticas de cada marketplace. | [Demo ao vivo](#) |

> Os links de demo ficam ativos assim que o GitHub Pages deste repositório for habilitado (Settings → Pages → branch `main` → pasta `/`).

## Stack

HTML + CSS + JavaScript puro. Sem frameworks, sem build step, sem `node_modules` — abrir o `index.html` já funciona, e o deploy é só `git push`.

## Rodando localmente

Basta abrir o `index.html` de qualquer projeto no navegador, ou servir a pasta com qualquer servidor estático:

```bash
python -m http.server 8000
```

## Licença

MIT — veja [LICENSE](LICENSE).