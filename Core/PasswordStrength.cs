namespace Arcanum.Core;

public static class PasswordStrength
{
    public enum Level { Fraca, Media, Forte }

    public static string? ValidatePolicy(string pw)
    {
        if (pw.Length < 12)                          return "Mínimo de 12 caracteres.";
        if (!pw.Any(char.IsUpper))                   return "Inclua ao menos uma letra maiúscula.";
        if (!pw.Any(char.IsLower))                   return "Inclua ao menos uma letra minúscula.";
        if (!pw.Any(char.IsDigit))                   return "Inclua ao menos um número.";
        if (!pw.Any(c => !char.IsLetterOrDigit(c)))  return "Inclua ao menos um caractere especial.";
        return null;
    }

    public static (int Score, Level Level, string Label) Evaluate(string? password)
    {
        if (string.IsNullOrEmpty(password)) return (0, Level.Fraca, "");

        int score = 0;

        score += Math.Min(password.Length * 3, 45);

        if (password.Any(char.IsUpper))                  score += 10;
        if (password.Any(char.IsLower))                  score += 10;
        if (password.Any(char.IsDigit))                  score += 15;
        if (password.Any(c => !char.IsLetterOrDigit(c))) score += 20;

        score = Math.Min(score, 100);

        var level = score < 40 ? Level.Fraca : score < 70 ? Level.Media : Level.Forte;
        var label = level switch { Level.Fraca => "Fraca", Level.Media => "Média", _ => "Forte" };

        return (score, level, label);
    }
}
