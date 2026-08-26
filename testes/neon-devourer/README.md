# Testes do Neon Devourer

Rede de proteção do jogo. Roda em Node puro, sem instalar nada.

```bash
cd testes/neon-devourer
node run.js              # tudo (maratona curta, 20 fases)
node run.js --completo   # maratona de 60 fases
node run.js --offline    # pula a suíte que precisa de internet
node 2-blindagem.js      # uma suíte isolada
```

Testa `minijogos/neon-devourer/index.html`. Para apontar para outro arquivo:
`NEON_HTML=/caminho/index.html node run.js`

## As suítes

| Arquivo | O que garante |
|---|---|
| `1-basico.js` | o jogo sobe, todo núcleo é alcançável a pé, os mobs saem do ninho e não entram em parede, SHIFT atordoa e ESPAÇO dá dash, mob atordoado é atravessável |
| `2-blindagem.js` | 14 defeitos reais de rede que já aconteceram — cada teste reproduz um |
| `3-balanco.js` | a dificuldade sobe depois da fase 10, o uptime das habilidades fica na faixa, o ímã não atravessa parede, a sentinela só atira com corredor livre |
| `4-maratona.js` | 60 fases seguidas sem mapa impossível, ninho isolado, entidade presa em parede ou vazamento |
| `5-online.js` | duas instâncias do jogo jogando juntas **pela internet de verdade**, e o RLS do ranking barrando escrita com a chave pública |

## Como funciona

`harness.js` sobe o jogo num contexto Node isolado (`vm`) com um DOM e um canvas
de mentira. Isso testa a lógica; **não** testa pixel, FPS nem toque — para isso é
Chrome DevTools.

## Armadilhas registradas

**Dois `<script>` no arquivo.** O jogo tem o bloco do modo vitrine (`?attract=1`)
e o do jogo. Uma regex gulosa pega do primeiro `<script>` até o último
`</script>` e engole a tag do meio, quebrando com erro de sintaxe. O harness
sempre pega o **maior** bloco.

**`COLS` e `ROWS` são exportados por valor.** O snapshot congela no boot e não
acompanha a troca de grade. Use `X.grade()`, que devolve os valores atuais.
Três testes já falharam por isso — e o jogo estava certo.

**O keepalive anda por relógio de parede.** Um laço apertado roda mil iterações
no mesmo milissegundo e nada dispara. Os testes de `tickRede` esperam de verdade
com `setTimeout`.

**`5-online.js` precisa do projeto Supabase no ar.** Se estiver pausado, a suíte
se pula sozinha e avisa — isso não é falha do jogo. O plano é gratuito e pausa
por inatividade.
