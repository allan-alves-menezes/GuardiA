using System.Text.RegularExpressions;
using GuardiA.Core.Interfaces;

namespace GuardiA.Core.Services;

public class RiskAnalysisService : IRiskAnalysisService
{
    private readonly ILogger<RiskAnalysisService> _logger;

    public RiskAnalysisService(ILogger<RiskAnalysisService> logger)
    {
        _logger = logger;
    }

    public Task<LinkAnalysisResultDto> AnalyzeLinkAsync(string urlString)
    {
        var findings = new List<string>();
        double riskScore = 0.0;

        if (!Uri.TryCreate(urlString, UriKind.Absolute, out var uri))
        {
            return Task.FromResult(new LinkAnalysisResultDto(urlString, true, 100.0, ["URL inválida ou malformada."]));
        }

        // Verificação 1: Uso de IP direto na URL (comum em phishing)
        if (System.Net.IPAddress.TryParse(uri.Host, out _))
        {
            riskScore += 40.0;
            findings.Add("A URL utiliza um endereço IP direto em vez de um nome de domínio.");
        }

        // Verificação 2: Presença de caracteres suspeitos ou múltiplos subdomínios
        if (uri.Host.Split('.').Length > 4)
        {
            riskScore += 25.0;
            findings.Add("O domínio possui múltiplos subdomínios, o que pode indicar técnica de mascaramento.");
        }

        // Verificação 3: Palavras-chave comumente usadas em golpes
        string[] suspiciousKeywords = ["login", "verify", "update", "secure", "account", "banking"];
        if (suspiciousKeywords.Any(keyword => uri.AbsolutePath.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
        {
            riskScore += 20.0;
            findings.Add("O caminho da URL contém termos frequentemente associados a páginas de login falsas.");
        }

        bool isSuspicious = riskScore >= 50.0;
        _logger.LogInformation("Análise de link concluída para {Url}. Risco: {Score}", urlString, riskScore);

        return Task.FromResult(new LinkAnalysisResultDto(urlString, isSuspicious, riskScore, findings));
    }

    public Task<EmailHeaderAnalysisResultDto> AnalyzeEmailHeaderAsync(string rawHeader)
    {
        var warnings = new List<string>();
        bool isAuthentic = true;
        string originIp = "Desconhecido";

        try
        {
            // Extração simplificada de IP de origem via Regex nos cabeçalhos 'Received'
            var ipMatch = Regex.Match(rawHeader, @"from\s+.*?\[(\d{1,3}(?:\.\d{1,3}){3})\]", RegexOptions.IgnoreCase);
            if (ipMatch.Success)
            {
                originIp = ipMatch.Groups[1].Value;
            }

            // Verificação de falhas de autenticação comuns (SPF / DKIM)
            if (rawHeader.Contains("spf=fail", StringComparison.OrdinalIgnoreCase) || 
                rawHeader.Contains("dkim=fail", StringComparison.OrdinalIgnoreCase))
            {
                isAuthentic = false;
                warnings.Add("Falha detectada nas validações de SPF ou DKIM do cabeçalho.");
            }

            if (!rawHeader.Contains("Authentication-Results", StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add("Cabeçalho 'Authentication-Results' ausente ou inconclusivo.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao processar cabeçalho de e-mail.");
            warnings.Add("Erro interno ao analisar a estrutura do cabeçalho.");
            isAuthentic = false;
        }

        return Task.FromResult(new EmailHeaderAnalysisResultDto(isAuthentic, originIp, warnings));
    }
}