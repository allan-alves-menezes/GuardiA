using System.Text.RegularExpressions;

namespace GuardiaProjeto.Core.Service
{
    public static class TyposquattingService
    {
        public static List<string> GerarVariacoes(string dominioBase)
        {
            var variacoes = new HashSet<string>();
            
            // Limpar o domínio (remover http://, https://, www.)
            var limpo = Regex.Replace(dominioBase ?? string.Empty, @"^(https?://)?(www\.)?", "").TrimEnd('/');
            
            if (string.IsNullOrEmpty(limpo))
                return new List<string>();

            // Separar nome e extensão (ex: google.com -> google e .com)
            int lastDot = limpo.LastIndexOf('.');
            if (lastDot <= 0) return variacoes.ToList();

            string nome = limpo.Substring(0, lastDot);
            string extensao = limpo.Substring(lastDot);

            // 1. Variação por Omissão de Caracteres
            for (int i = 0; i < nome.Length; i++)
            {
                string omissao = nome.Remove(i, 1);
                if (omissao.Length > 1) variacoes.Add($"{omissao}{extensao}");
            }

            // 2. Variação por Duplicação de Caracteres
            for (int i = 0; i < nome.Length; i++)
            {
                string duplicacao = nome.Insert(i, nome[i].ToString());
                variacoes.Add($"{duplicacao}{extensao}");
            }

            // 3. Variação por Extensões Populares (TLD Hijacking / Typosquatting de Domínio)
            string[] tldsComuns = [".com", ".com.br", ".net", ".org", ".co", ".io", ".security"];
            foreach (var tld in tldsComuns)
            {
                if (tld != extensao)
                {
                    variacoes.Add($"{nome}{tld}");
                }
            }

            // Remover o próprio domínio original da lista se por acaso entrar
            variacoes.Remove(limpo);

            return variacoes.Take(15).ToList(); // Retorna até 15 variações principais
        }
    }
}