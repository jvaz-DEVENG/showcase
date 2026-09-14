namespace GameBoost.Core.Abstractions;

public enum RegistryRoot
{
    ClassesRoot,
    CurrentUser,
    LocalMachine,
    Users
}

/// <summary>
/// Acesso ao registro. Toda escrita exige que o chamador ja tenha gravado o
/// valor anterior num ChangeRecord (regra 1).
/// </summary>
public interface IRegistryService
{
    object? GetValue(RegistryRoot root, string subKey, string valueName);
    void SetValue(RegistryRoot root, string subKey, string valueName, object value, RegistryValueKindLite kind);
    void DeleteValue(RegistryRoot root, string subKey, string valueName);
    bool KeyExists(RegistryRoot root, string subKey);
    IReadOnlyList<string> GetSubKeyNames(RegistryRoot root, string subKey);
    IReadOnlyList<string> GetValueNames(RegistryRoot root, string subKey);
}

public enum RegistryValueKindLite
{
    String,
    ExpandString,
    DWord,
    QWord,
    Binary,
    MultiString
}
