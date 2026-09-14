# Portas por jogo e por launcher

Referência usada pela página **Rede** quando o diagnóstico encontra NAT estrito e
o roteador não tem UPnP. É a lista que o usuário precisa ter na mão para abrir
porta à mão, e o texto de cada seção diz o que abrir e o que **não** adianta
abrir.

> **Antes de abrir qualquer porta:** reserve um IP fixo para este PC no DHCP do
> roteador. Sem isso o endereço muda no próximo boot e a regra passa a apontar
> para outra máquina da casa — normalmente a TV.

> **Abrir porta não resolve CGNAT nem NAT duplo.** Se o diagnóstico apontou um
> desses, a porta aberta no seu roteador morre no equipamento de cima. Veja as
> seções no fim deste arquivo.

---

## O que o GameBoost faz e o que ele não faz

| | |
|---|---|
| Detecta o tipo de NAT (STUN) | Sim |
| Detecta CGNAT, NAT duplo, UPnP, Teredo e firewall | Sim |
| Reativa o Teredo (reversível) | Sim |
| Abre porta via UPnP com prazo de 24 h | Previsto para a Fase 6, junto com o detector de jogo |
| Configura o roteador | **Não.** O roteador não é dele; uma regra escrita lá não tem como ser desfeita se o app sumir |
| Coloca o modem em bridge | **Não.** Isso derruba a internet da casa até a nova configuração subir |

---

## Launchers e plataformas

Estas são as portas que valem para **todos** os jogos da plataforma. Abrir só
estas já resolve boa parte dos casos de "não entro na festa".

### Steam

| Porta | Protocolo | Para quê |
|---|---|---|
| 27015 – 27030 | UDP | Tráfego de jogo e consulta de servidor |
| 27015 – 27030 | TCP | Servidor dedicado |
| 27036 – 27037 | UDP e TCP | Remote Play e transmissão na rede local |
| 4380 | UDP | Tráfego de jogo antigo (Source) |
| 3478, 4379, 4380 | UDP | Voz do Steam |

### Epic Games

| Porta | Protocolo | Para quê |
|---|---|---|
| 5222 | TCP | Presença e convite de amigo |
| 5795 – 5847 | UDP | Tráfego de jogo (Fortnite e outros) |
| 443, 80 | TCP | Download e autenticação |

### Battle.net (Blizzard)

| Porta | Protocolo | Para quê |
|---|---|---|
| 1119 | TCP e UDP | Battle.net (chat, grupo, presença) |
| 3724 | TCP | World of Warcraft |
| 6112 – 6114 | TCP e UDP | Jogos antigos e Overwatch |
| 80, 443 | TCP | Cliente e download |

### Xbox / Game Pass no PC

| Porta | Protocolo | Para quê |
|---|---|---|
| 3074 | UDP e TCP | Rede Xbox Live |
| 88 | UDP | Autenticação |
| 500, 3544, 4500 | UDP | **Teredo** — sem estas, festa e multiplayer não conectam |
| 53 | UDP e TCP | DNS |

> O caso do Game Pass é o mais comum de todos: o multiplayer não conecta porque
> alguém desativou o Teredo seguindo um tutorial de "otimização de rede". O
> GameBoost detecta isso e reativa, com desfazer. Não precisa abrir porta
> nenhuma para resolver esse caso.

### Riot (League of Legends, Valorant)

| Porta | Protocolo | Para quê |
|---|---|---|
| 5000 – 5500 | UDP | Tráfego de partida |
| 8393 – 8400 | TCP | Cliente |
| 2099, 5222, 5223 | TCP | Chat e plataforma |

### EA / Origin / EA App

| Porta | Protocolo | Para quê |
|---|---|---|
| 3659 | UDP | Tráfego de jogo |
| 9988, 20000 – 20100 | UDP | Matchmaking |
| 42127 | TCP | Cliente |
| 1024 – 1124 | TCP | Download |

### GOG Galaxy

Não exige porta aberta: usa relay por padrão. Se um jogo específico da GOG pedir
porta, ela é do jogo, não da plataforma.

### Discord

| Porta | Protocolo | Para quê |
|---|---|---|
| 50000 – 65535 | UDP | Voz |
| 443, 80 | TCP | Aplicativo |

> Discord com voz falhando e NAT estrito no diagnóstico são o mesmo problema.
> Quando não dá para abrir porta, a opção "Qualidade de serviço com prioridade
> alta" **desligada** nas configurações de voz do Discord costuma ajudar, porque
> alguns roteadores tratam mal o pacote marcado.

---

## Jogos com porta própria

Abrir a porta do launcher normalmente basta. Estes pedem porta própria quando
você quer **hospedar**.

| Jogo | Portas | Protocolo |
|---|---|---|
| Minecraft (Java, servidor) | 25565 | TCP e UDP |
| Minecraft (Bedrock) | 19132 – 19133 | UDP |
| Terraria | 7777 | TCP |
| ARK: Survival Evolved | 7777 – 7778, 27015 | UDP |
| Valheim | 2456 – 2458 | UDP |
| Rust | 28015 – 28016 | UDP |
| Call of Duty (Warzone, MW) | 3074, 27014 – 27050 | UDP e TCP |
| Counter-Strike 2 | 27015 – 27020, 27031 – 27036 | UDP |
| Palworld | 8211 | UDP |
| Satisfactory | 7777, 15000, 15777 | UDP |

---

## CGNAT: por que abrir porta não adianta

A operadora divide um endereço público entre vários assinantes. O NAT que
atrapalha está no equipamento dela, e você não tem acesso a ele. O sinal é o IP
de WAN do seu roteador estar na faixa `100.64.x.x` a `100.127.x.x`, ou ser
diferente do IP que a internet enxerga.

O que funciona, em ordem de esforço:

1. **Pedir IP público à operadora.** Em várias é gratuito e resolve por telefone;
   em outras é um adicional mensal. O nome varia: "IP público", "IP fixo", "IP
   válido", "saída do CGNAT".
2. **IPv6.** Jogos que suportam IPv6 passam por cima do CGNAT, porque cada
   máquina tem endereço próprio. A maioria das operadoras brasileiras já entrega
   IPv6; verifique se está ligado no roteador.
3. **Serviço de roteamento** (ExitLag, WTFast, GearUP). Custa por mês, e não
   resolve em todo jogo — só nos que aceitam o túnel. É paliativo, não solução.

O que **não** funciona: abrir porta, ligar UPnP, DMZ, trocar de DNS, resetar o
Winsock, trocar o cabo. Nenhum deles toca no equipamento da operadora.

---

## NAT duplo: dois roteadores em série

Acontece quando o modem da operadora também roteia e você ligou o seu roteador
nele. O `tracert` mostra dois ou mais saltos com IP privado antes do primeiro
público.

Três saídas, da melhor para a pior:

1. **Modo bridge no modem da operadora.** Ele passa a ser só modem e o seu
   roteador recebe o IP público direto. É a solução correta. Feito na
   configuração do modem, e às vezes só o suporte da operadora consegue ligar.
2. **DMZ no modem apontando para o IP do seu roteador.** Todo tráfego de entrada
   passa a ir para o seu roteador. Funciona, mas expõe o seu roteador inteiro.
3. **Usar só um dos dois.** Se o modem da operadora tem Wi-Fi decente, desligar o
   seu roteador resolve o NAT duplo de graça.

---

## Marcas de roteador: onde fica cada coisa

O endereço é quase sempre `192.168.0.1`, `192.168.1.1` ou `192.168.15.1` — a
página Rede mostra o do seu. O usuário e a senha padrão costumam estar numa
etiqueta embaixo do aparelho.

| Marca | UPnP | Redirecionamento de porta |
|---|---|---|
| TP-Link | Avançado → NAT Forwarding → UPnP | Avançado → NAT Forwarding → Virtual Servers |
| Intelbras | Avançado → Encaminhamento → UPnP | Avançado → Encaminhamento → Servidores Virtuais |
| Huawei | Avançado → NAT → UPnP | Avançado → NAT → Mapeamento de Portas |
| ZTE | Aplicação → UPnP | Aplicação → Encaminhamento de Portas |
| Mercusys | Avançado → NAT Forwarding → UPnP | Avançado → NAT Forwarding → Servidores Virtuais |
| Asus | WAN → Internet Connection → Enable UPnP | WAN → Virtual Server / Port Forwarding |
| D-Link | Advanced → Advanced Network → UPnP | Advanced → Port Forwarding |

---

## Quando o problema é o firewall do Windows, não o roteador

Dois casos, e os dois aparecem no diagnóstico:

- **Rede marcada como Pública.** O Windows bloqueia entrada por padrão em rede
  pública. Em casa, a rede deve estar como Privada.
- **O jogo não tem regra de entrada.** Acontece quando o jogo foi instalado
  copiando a pasta, ou quando alguém clicou em "Cancelar" no aviso do firewall
  na primeira execução.

O GameBoost hoje lê e informa os dois. Criar a regra do jogo e trocar o perfil da
rede dependem de saber qual jogo está aberto, o que entra na Fase 6 junto com o
detector de jogo.

---

## Fontes

As portas vêm da documentação de suporte de cada plataforma (Steam, Epic,
Blizzard, Riot, EA, Xbox) consultadas em setembro de 2026. Elas mudam pouco, mas
mudam: quando um jogo parar de bater com esta tabela, a fonte é a página de
suporte do próprio jogo, não um fórum.
