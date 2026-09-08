namespace Mostik.Core;

public sealed record Direction(string Source, string Target, string Label)
{
    public static readonly Direction RuToPl = new("ru", "pl", "Русский → польский");
    public static readonly Direction PlToRu = new("pl", "ru", "Польский → русский");

    public static Direction FromSource(string code) => code switch
    {
        "ru" => RuToPl,
        "pl" => PlToRu,
        _ => throw new ArgumentException("Поддерживаются только русский и польский.", nameof(code))
    };
}
