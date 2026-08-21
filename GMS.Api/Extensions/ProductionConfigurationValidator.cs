namespace GMS.Api.Extensions;

/// <summary>Fail fast with clear console output when Production hosting is misconfigured (MonsterASP / IIS).</summary>
public static class ProductionConfigurationValidator
{
    public static void Validate(IConfiguration configuration, IWebHostEnvironment environment)
    {
        if (!environment.IsProduction())
            return;

        var errors = new List<string>();

        var cs = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(cs))
            errors.Add("ConnectionStrings:DefaultConnection is missing. Set ConnectionStrings__DefaultConnection in MonsterASP environment variables.");
        else if (cs.Contains("YOUR_MONSTERASP", StringComparison.OrdinalIgnoreCase)
                 || cs.Contains("your-azure-server", StringComparison.OrdinalIgnoreCase))
            errors.Add("DefaultConnection still contains placeholder text. Set your MonsterASP Cloud SQL connection string in environment variables.");

        var jwt = configuration["JwtSettings:SecretKey"];
        if (string.IsNullOrWhiteSpace(jwt))
            errors.Add("JwtSettings:SecretKey is empty. Set JwtSettings__SecretKey (64+ random characters) in MonsterASP environment variables.");
        else if (jwt.Length < 32)
            errors.Add("JwtSettings:SecretKey must be at least 32 characters.");

        if (errors.Count > 0)
        {
            var message = "GymFlowPro Production configuration error(s):" + Environment.NewLine
                + string.Join(Environment.NewLine, errors.Select(e => " - " + e));
            Console.Error.WriteLine(message);
            throw new InvalidOperationException(message);
        }
    }
}
