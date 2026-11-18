namespace GuardiA.Core.Interfaces;

public interface IRiskAnalysisService
{
    Task<LinkAnalysisResultDto> AnalyzeLinkAsync(string url);
    Task<EmailHeaderAnalysisResultDto> AnalyzeEmailHeaderAsync(string rawHeader);
}

public record LinkAnalysisResultDto(string Url, bool IsSuspicious, double RiskScore, List<string> Findings);
public record EmailHeaderAnalysisResultDto(bool IsAuthentic, string OriginIp, List<string> Warnings);