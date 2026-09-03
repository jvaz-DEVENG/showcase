# PADRÃO MINIJOGOS — doutrina do estúdio

Fonte da verdade para todo minijogo deste repositório. **NÚCLEO PRIME** e os
subagentes (FORGE, MAPA, LOOP, UI, BALANCE, QA, ART, MULTI, LORE) leem este
arquivo antes de qualquer decisão. Se algo aqui conflita com uma ideia solta,
este arquivo vence — ou o arquivo muda primeiro, de propósito.

---

## Regra zero

Nunca entregue um jogo genérico, simples demais ou sem profundidade. Mesmo
inspirado num clássico, ele vira **uma experiência nova**: nova leitura de
gameplay, novos recursos, novas interações, novo ritmo, novo visual, novo
sistema de progressão.

Na dúvida entre o simples e o memorável, escolha o **memorável, original e
completo**. Cada jogo precisa parecer parte de uma marca — não um teste isolado.

---

## As 10 regras de decisão

1. Todo jogo tem uma **mecânica assinatura** exclusiva.
2. Todo jogo tem pelo menos um **sistema de progressão** real.
3. Interface limpa, legível e organizada.
4. Feedback visual forte e sensação de impacto.
5. Identidade **diferente dos jogos anteriores** do repo.
6. Arquitetura expansível.
7. Testado antes de finalizado (QA obrigatório).
8. Nenhum sistema existe só por enfeite.
9. Se não melhora o jogo, não entra.
10. O padrão do estúdio é sempre reconhecível.

---

## Restrições técnicas deste repositório (inegociáveis)

Estas não são preferências — são o contrato do showcase. Quebrou, o jogo não
entra no portfólio.

- **Um `index.html` único e autocontido** em `minijogos/<slug>/`, com CSS e JS
  embutidos.

  **Dependência não é proibida** — o dono decidiu isso em 2026-09-03. O que é
  inegociável é o *jeito*: biblioteca entra **embutida no arquivo** (colada,
  vendorizada), nunca por CDN e nunca por build step. O motivo é técnico, não
  estético:
  - **CDN quebra o jogo local.** O dono joga por `file://`, e sob esse protocolo
    o navegador bloqueia a maior parte do que vem de fora. Também mataria o
    funcionamento offline e faria cada card do portfólio depender de rede.
  - **Build step quebra o deploy.** O fluxo do repo é "abrir o index.html já
    funciona" e "deploy é só `git push`". Foi por isso que o Apollo Tanks, que
    precisa de servidor Node, ficou fora do portfólio.

  Onde uma dependência vale a pena de verdade hoje: **WebGL para render**. O
  gargalo de desempenho do Neon Devourer é o `shadowBlur` do canvas 2D (o
  brilho neon), que em shader sai praticamente de graça. E **Capacitor**, que é
  obrigatório para empacotar na Play Store.

  Áudio segue sintetizado em Web Audio, nunca `.mp3`/`.wav` — aqui não é regra
  imposta, é que síntese faz melhor o que estes jogos precisam.
- **Roda sob `file://`** — abrir o arquivo no navegador tem que funcionar.
  Nada de `fetch` para caminho relativo, `import` de módulo ou canvas com
  imagem externa (origem opaca).
- **Modo vitrine `?attract=1`**: piloto automático joga sozinho em loop, título
  e rodapé somem, e `Storage.prototype.setItem` vira no-op para o bot nunca
  gravar por cima do recorde de quem joga. A vitrine informa o próprio tamanho
  ao portfólio por `postMessage`.
- **`poster.png`** na pasta do jogo (usado quando `prefers-reduced-motion`).
- **Card no `index.html` da raiz** + **linha na tabela do `README.md`**.
- **Rodapé assinado** com semver (`vMAJOR.MINOR.PATCH`) e link para
  [@jvaz-DEVENG](https://github.com/jvaz-DEVENG). PATCH = correção,
  MINOR = funcionalidade, MAJOR = quebra save salvo.
- **Save em `localStorage`** com chave prefixada pelo slug do jogo, e leitura
  tolerante a dado ausente ou corrompido (nunca quebrar na primeira abertura).
- **`prefers-reduced-motion` respeitado** e **mobile responsivo** (toque
  funciona, HUD não estoura em tela pequena).
- **Performance**: 60 FPS alvo, com fundo/arte estática assados em bitmap
  cacheado em vez de redesenhados por frame. Pausa quando a aba fica escondida.
- **Identidade dos minijogos usa emoji** (as *ferramentas* é que usam ícone de
  linha). Tom descontraído.
- O **repo é a fonte da verdade do jogo** — nunca copiar por cima de um
  `index.html` existente sem antes ler o que já está lá. O link público é o
  **GitHub Pages**, `https://jvaz-deveng.github.io/showcase/minijogos/<slug>/`.

---

## Anatomia obrigatória de um jogo

### 1. Identidade e conceito
Nome original, tema central forte, estilo visual coerente, lore curta que dê
personalidade.

### 2. Loop principal
O jogador entende em segundos o que faz. Começo, meio e fim claros. Divertido
no curto e no longo prazo. Desafio crescente e sensação de evolução.

### 3. Mecânica assinatura
Pelo menos uma coisa que só este jogo faz. Exemplos de direção: absorver
energia, trocar de forma, atravessar paredes, manipular o tempo, invocar
aliados, combinar elementos, ou um recurso próprio (carga, combo, calor, mana,
essência).

### 4. Poderes e habilidades
Ativas + passivas, com cooldown/recarga, upgrades, efeito visual e sonoro
claro, e **sinergia entre habilidade e mapa**.

### 5. Progressão
XP, níveis, talentos, desbloqueios, moedas, conquistas, fases de dificuldade
crescente, melhorias permanentes.

### 6. Mapa e fase
Layouts diferentes por fase, variações visuais **e mecânicas**, obstáculos
próprios por bioma, caminhos alternativos/armadilhas/atalhos/eventos. Variedade
real entre fases, não paleta trocada.

### 7. Inimigos, obstáculos e chefes
Comportamentos distintos, IA variada, ao menos um chefe. Padrões fáceis de
entender e difíceis de dominar, coerentes com o tema e a mecânica assinatura.

### 8. Campanha
Sequência de fases, progressão de dificuldade, desbloqueio de recursos,
objetivo claro por etapa, conclusão satisfatória.

### 9. Loja
Skins, cosméticos, bônus, melhorias, efeitos, desbloqueios — com moeda ganha
jogando. Sem compra real, sem padrão escuro.

### 10. Menu, configurações e manual
Iniciar campanha · continuar · multiplayer · loja · configurações · manual de
como jogar · recordes/ranking · créditos.

### 11. Multiplayer (quando couber ao gênero)
Local, coop ou versus. Regras claras, divertido sozinho **e** em grupo, HUD e
feedback para múltiplos jogadores. Online só quando o gênero pedir de verdade.

### 12. Interface
Moderna, limpa, legível, responsiva, consistente com o tema.

---

## Referência de filosofia

**Neon Devourer** e **Cosmic Crush** são a régua: arcade com cara própria,
progressão real, biomas diferentes, mapa por fase, habilidades ativas,
talentos, skins, loja, conquistas, menu bem organizado, sistema completo e
funcional. **Não copie** — iguale o capricho, invente a experiência.

---

## Entrega mínima quando o usuário pede um jogo novo

Nome · conceito · mecânica principal · mecânica exclusiva · poderes e passivas ·
progressão · campanha · loja · menu e manual · multiplayer (se aplicável) ·
UI/HUD · identidade visual. E o **código completo pronto para executar** quando
pedido.
