namespace GameBoost.Core.Modules.Bottleneck;

/// <summary>
/// Janela deslizante dos ultimos 5 minutos (secao 5.5). Tamanho fixo: o buffer
/// nunca cresce, entao a coleta continua cabendo no orcamento de 60 MB.
/// </summary>
public sealed class MetricsBuffer
{
    private readonly MetricsSnapshot?[] _itens;
    private readonly object _gate = new();
    private int _proximo;
    private int _quantidade;

    public MetricsBuffer(int capacidade = 300)
    {
        if (capacidade <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacidade));

        Capacidade = capacidade;
        _itens = new MetricsSnapshot?[capacidade];
    }

    public int Capacidade { get; }

    public int Quantidade
    {
        get { lock (_gate) return _quantidade; }
    }

    public MetricsSnapshot? Ultimo
    {
        get
        {
            lock (_gate)
            {
                if (_quantidade == 0)
                    return null;

                var indice = (_proximo - 1 + Capacidade) % Capacidade;
                return _itens[indice];
            }
        }
    }

    public void Adicionar(MetricsSnapshot snapshot)
    {
        lock (_gate)
        {
            _itens[_proximo] = snapshot;
            _proximo = (_proximo + 1) % Capacidade;

            if (_quantidade < Capacidade)
                _quantidade++;
        }
    }

    /// <summary>Do mais antigo para o mais recente.</summary>
    public IReadOnlyList<MetricsSnapshot> Todos()
    {
        lock (_gate)
        {
            var resultado = new List<MetricsSnapshot>(_quantidade);
            var inicio = (_proximo - _quantidade + Capacidade) % Capacidade;

            for (var i = 0; i < _quantidade; i++)
            {
                var item = _itens[(inicio + i) % Capacidade];
                if (item is not null)
                    resultado.Add(item);
            }

            return resultado;
        }
    }

    /// <summary>Os ultimos N segundos, para o grafico e para as regras de duracao.</summary>
    public IReadOnlyList<MetricsSnapshot> Ultimos(TimeSpan janela)
    {
        var todos = Todos();
        if (todos.Count == 0)
            return todos;

        var corte = todos[^1].Momento - janela;
        return todos.Where(s => s.Momento >= corte).ToList();
    }

    public void Limpar()
    {
        lock (_gate)
        {
            Array.Clear(_itens);
            _proximo = 0;
            _quantidade = 0;
        }
    }
}
