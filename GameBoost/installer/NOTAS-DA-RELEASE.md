## O que baixar

| Arquivo | Para quem |
|---|---|
| `GameBoost-Setup-*.exe` | A maioria. Instala, cria atalho e habilita a atualização automática |
| `GameBoost-*-portatil.zip` | Pen drive, máquina de terceiro, ou quem não quer instalar nada. Guarda os dados ao lado do executável e **não** se atualiza sozinho |
| `GameBoost.exe` | O executável solto, para quem sabe o que quer |
| `SHA256SUMS.txt` | Conferir que o download veio inteiro |

Os arquivos `.nupkg` e `*-full.nupkg` são do mecanismo de atualização. Não
precisam ser baixados à mão.

## Aviso do SmartScreen

Esta versão pode não estar assinada. Se aparecer *"O Windows protegeu o
computador"*, o caminho é **Mais informações → Executar assim mesmo**.

Antes disso, se quiser conferir que o arquivo é o mesmo que saiu daqui:

```powershell
Get-FileHash .\GameBoost-Setup-2.0.0.exe -Algorithm SHA256
```

e comparar com a linha correspondente do `SHA256SUMS.txt`.

O porquê de não estar assinado, e o que está sendo feito a respeito, está em
[docs/ASSINATURA.md](../docs/ASSINATURA.md).

## O que ele faz

Todo o resto está no [README](../README.md). O resumo em três linhas:

- Tudo o que ele altera, ele desfaz. O valor anterior vai para o histórico antes
  da escrita, e cada ação tem botão de desfazer.
- Não tem telemetria. Nenhum dado sai da máquina.
- Quando um ajuste é marginal, está escrito que é marginal.
