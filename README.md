# Showcase

Um repositório único com minijogos e ferramentas práticas — cada um roda direto no navegador, sem instalação, sem dependências e sem servidor. Feito pra ser jogado/testado com um clique via GitHub Pages.

Cada projeto vive na sua própria pasta e é 100% autocontido (`index.html` único com CSS e JS embutidos).

## 🎮 Minijogos

| Projeto | Descrição | Jogar |
|---|---|---|
| [Reflex Rush](minijogos/reflex-rush/) | Jogo de reflexo: acerte alvos que somem rápido, monte combos, 30 segundos por rodada, recorde salvo localmente. | [Demo ao vivo](https://jvaz-deveng.github.io/showcase/minijogos/reflex-rush/) |
| [Fusion Rush](minijogos/fusion-rush/) | Física de merge (bolhas iguais se fundem numa maior) com combo por velocidade, modo sem cronômetro (só acaba se o pote transbordar) e um gato ativo tentando roubar bolhas pelas laterais — acerta e você ganha uma bola de força que sacode a pilha, erra 5 vezes seguidas e ele se estressa e estoura sua maior bolha. Desafio diário compartilhável estilo Wordle. | [Demo ao vivo](https://jvaz-deveng.github.io/showcase/minijogos/fusion-rush/) |
| [Mata Barata](minijogos/mata-barata/) | Whack-a-mole: esmague as baratas antes que fujam, monte combo (reseta se uma escapar), dourada vale 5x mais. Dificuldade sobe conforme você acerta. 30 segundos por rodada. | [Demo ao vivo](https://jvaz-deveng.github.io/showcase/minijogos/mata-barata/) |

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