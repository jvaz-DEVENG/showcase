using System.Text.Json;
using GameBoost.Core.Abstractions;
using GameBoost.Core.Logging;
using GameBoost.Core.Settings;

namespace GameBoost.Core.Modules.Profiles;

/// <summary>
/// Perfis em `profiles/*.json`, um arquivo por jogo (seção 7).
///
/// Um arquivo por perfil, e não um só com todos: perfil corrompido derruba
/// aquele jogo, não a coleção inteira. É o mesmo motivo de o `state-backup.json`
/// ser separado do `settings.json`.
/// </summary>
public sealed class ProfileStore
{
    private readonly AppPaths _paths;
    private readonly IFileSystem _fs;
    private readonly IGameBoostLogger _log;

    private readonly Dictionary<string, GameProfile> _perfis = new(StringComparer.OrdinalIgnoreCase);
    private bool _carregado;

    public ProfileStore(AppPaths paths, IFileSystem fs, IGameBoostLogger log)
    {
        _paths = paths;
        _fs = fs;
        _log = log;
    }

    public IReadOnlyCollection<GameProfile> Todos
    {
        get
        {
            Carregar();
            return _perfis.Values.OrderByDescending(p => p.UltimaVez ?? DateTimeOffset.MinValue).ToList();
        }
    }

    public GameProfile? Buscar(string executavel)
    {
        Carregar();
        return _perfis.GetValueOrDefault(GameProfile.Normalizar(executavel));
    }

    public void Salvar(GameProfile perfil)
    {
        Carregar();

        perfil.Executavel = GameProfile.Normalizar(perfil.Executavel);

        if (perfil.Executavel.Length == 0)
        {
            _log.Warn("profiles", "Salvar", perfil.Nome, "perfil sem executável, ignorado");
            return;
        }

        _perfis[perfil.Executavel] = perfil;

        try
        {
            Directory.CreateDirectory(_paths.ProfilesDirectory);

            _fs.WriteAllText(
                Arquivo(perfil.Executavel),
                JsonSerializer.Serialize(perfil, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Error("profiles", "Salvar", perfil.Executavel, ex.Message, ex);
        }
    }

    public void Remover(string executavel)
    {
        Carregar();

        var chave = GameProfile.Normalizar(executavel);
        _perfis.Remove(chave);

        try
        {
            var arquivo = Arquivo(chave);

            if (_fs.FileExists(arquivo))
                File.Delete(arquivo);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warn("profiles", "Remover", chave, ex.Message);
        }
    }

    public void Recarregar()
    {
        _carregado = false;
        _perfis.Clear();
        Carregar();
    }

    private string Arquivo(string executavel)
    {
        // O nome do executável vira nome de arquivo, então tudo que não for
        // letra, número, ponto, hífen ou sublinhado sai. Um jogo chamado
        // "cs:go" não pode virar um caminho inválido.
        var limpo = new string(executavel
            .Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_')
            .ToArray());

        return Path.Combine(_paths.ProfilesDirectory, limpo + ".json");
    }

    private void Carregar()
    {
        if (_carregado)
            return;

        _carregado = true;

        try
        {
            if (!Directory.Exists(_paths.ProfilesDirectory))
                return;

            foreach (var arquivo in Directory.EnumerateFiles(_paths.ProfilesDirectory, "*.json"))
            {
                try
                {
                    var perfil = JsonSerializer.Deserialize<GameProfile>(_fs.ReadAllText(arquivo));

                    if (perfil is null || perfil.Executavel.Length == 0)
                        continue;

                    _perfis[GameProfile.Normalizar(perfil.Executavel)] = perfil;
                }
                catch (JsonException ex)
                {
                    // Um perfil quebrado não impede os outros de carregar. O
                    // arquivo fica onde está para o usuário poder olhar.
                    _log.Warn("profiles", "Carregar", Path.GetFileName(arquivo), $"JSON inválido: {ex.Message}");
                }
            }

            _log.Info("profiles", "Carregar", null, $"{_perfis.Count} perfis");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Error("profiles", "Carregar", null, ex.Message, ex);
        }
    }
}
