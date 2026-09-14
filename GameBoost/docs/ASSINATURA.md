# Assinatura de código e falso positivo

O que foi apurado em setembro de 2026 sobre assinar o GameBoost, e o que fazer
enquanto ele não está assinado.

Isto importa mais para este programa do que para a média. Um app que encerra
processos, mexe no registro, altera serviços e purga a Standby List tem o perfil
de comportamento que antivírus heurístico marca. Sem assinatura, o SmartScreen
avisa no primeiro download e uma parte das pessoas desiste ali.

---

## As opções, e por que a escolha é essa

### Azure Artifact Signing (ex-Trusted Signing) — **fora do Brasil**

É a opção barata da Microsoft, a partir de US$ 9,99/mês, e seria a escolha óbvia
se desse.

Não dá. Para **desenvolvedor individual** a Microsoft só aceita Estados Unidos e
Canadá. Para **organização**, a lista é maior — EUA, Canadá, União Europeia,
Reino Unido e mais alguns — e **o Brasil não está nela**, nem como pessoa física
nem como jurídica.

Não é questão de preço nem de burocracia: é elegibilidade geográfica. Não vale
gastar tempo tentando.

### Certificado OV de uma CA — **o caminho viável**

Certum, Sectigo, DigiCert, GlobalSign. Faixa de US$ 70 a 300 por ano; a Certum
costuma ser a mais barata para desenvolvedor individual.

Desde junho de 2023 a chave privada **não pode mais ficar num arquivo `.pfx` no
seu disco**: ela tem que morar em token USB ou HSM em nuvem, e a CA fornece um
dos dois. Isso muda o CI — assinar deixa de ser "coloca o segredo e roda" e passa
a exigir ou um runner com o token plugado, ou a API do HSM da CA.

O `gameboost-release.yml` tem o passo de assinatura preparado para o caso `.pfx`,
que serve para HSM em nuvem com credencial exportável e para certificado de teste.
Para token USB físico, o passo precisa ser trocado pelo cliente da CA.

### EV — **não vale mais a pena**

Até 2024 o certificado EV pulava o SmartScreen na hora. **Não pula mais.** Desde
então OV e EV passam pelo mesmo processo de reputação, e a diferença de preço
deixou de comprar alguma coisa.

Se alguém sugerir EV "porque tira o aviso", essa informação está desatualizada.

### Microsoft Store (MSIX) — **meta de médio prazo**

A Microsoft reassina o pacote, e o SmartScreen nunca aparece. É o caminho mais
limpo do ponto de vista do usuário.

Custa passar na certificação, e este app tem tudo para gerar pergunta: o Modo
Game encerra processos de terceiros, o desinstalador roda executáveis de
terceiros, e vários módulos escrevem em `HKLM`. Precisa de `runFullTrust`, e
ainda assim pode receber pedido de justificativa.

Vale tentar depois que a v2 estiver estável, não antes.

---

## Reputação: o que realmente tira o aviso

Assinar não zera o SmartScreen. O que zera é **reputação acumulada**, e ela
depende de duas coisas:

1. **Sempre a mesma identidade.** Trocar de certificado ou de nome de publicador
   reinicia a contagem. Se um dia o certificado for renovado, renovar com a mesma
   entidade, não com outra.
2. **Volume de downloads sem incidente.** Não há atalho. É tempo.

---

## Submeter falso positivo

Quando um antivírus marcar o GameBoost, o caminho é submeter a amostra, não pedir
ao usuário para desativar a proteção.

**Microsoft Defender** — [microsoft.com/wdsi/filesubmission](https://www.microsoft.com/en-us/wdsi/filesubmission)

Escolher "Software developer", anexar o `.exe`, e no campo de justificativa
descrever o que o programa faz e por quê. Vale citar, especificamente:

- Ele encerra processos porque é um otimizador de jogos e essa é a função
  principal, sempre com confirmação do usuário.
- Ele grava em `HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers` e chaves
  vizinhas porque aplica ajustes documentados pela própria Microsoft.
- Ele chama `NtSetSystemInformation` para purgar a Standby List.
- Todo o código está aberto em `github.com/jvaz-DEVENG/showcase`.

A resposta costuma vir em 24 a 72 horas.

**Outros antivírus** — cada um tem o próprio formulário. Os mais comuns em
máquina brasileira, em ordem de aparição: Kaspersky, Avast/AVG, Bitdefender,
McAfee, Norton. Todos aceitam submissão de desenvolvedor.

**Depois de cada release**, submeter de novo: a assinatura muda a cada build, e o
hash novo não herda a liberação do anterior.

---

## O que o usuário vê hoje

Sem certificado, no primeiro download:

> O Windows protegeu o computador.
> O Microsoft Defender SmartScreen impediu a inicialização de um aplicativo não
> reconhecido.

Para rodar mesmo assim: **Mais informações → Executar assim mesmo**.

O `SHA256SUMS.txt` sai em toda release justamente para isso: quem quiser conferir
que baixou o arquivo certo tem como, sem depender de assinatura nenhuma.
