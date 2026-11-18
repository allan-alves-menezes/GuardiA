// Funcao para alternar entre as abas do menu lateral
function switchTab(tabName) {
    document.querySelectorAll('.tab-content').forEach(tab => tab.classList.remove('active'));
    document.querySelectorAll('.nav-item').forEach(item => item.classList.remove('active'));

    document.getElementById(`tab-${tabName}`).classList.add('active');
    event.currentTarget.classList.add('active');
}

// Funcao para chamar a rota de varredura de links da API C#
async function executeUrlScan() {
    const urlInput = document.getElementById('scan-url-input').value;
    const resultBox = document.getElementById('scan-result');

    if (!urlInput) {
        alert('Por favor, informe uma URL.');
        return;
    }

    resultBox.classList.remove('hidden');
    resultBox.innerHTML = 'Aguarde... Processando varredura no servidor GuardIA.';

    try {
        const response = await fetch('/api/v1/scanner/url', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ url: urlInput })
        });

        const data = await response.json();
        resultBox.innerHTML = `
            <h4 style="color: #38bdf8; margin-bottom: 8px;">Resultado do Diagnóstico:</h4>
            <p><strong>URL Analisada:</strong> ${data.urlOriginal}</p>
            <p><strong>Status HTTP:</strong> ${data.codigoStatus}</p>
            <p><strong>Redirecionamento:</strong> ${data.possuiRedirecionamento ? 'SIM' : 'NÃO'}</p>
            <p style="margin-top: 8px; font-weight: bold; color: ${data.possuiRedirecionamento ? '#f43f5e' : '#4ade80'};">
                ${data.avaliacaoRisco}
            </p>
        `;
    } catch (error) {
        resultBox.innerHTML = `<span style="color: #f43f5e;">Erro ao conectar com a API: ${error.message}</span>`;
    }
}