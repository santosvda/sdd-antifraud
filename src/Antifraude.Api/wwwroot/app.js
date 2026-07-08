/* ============================================================================
   Console de Sinistros — cliente vanilla (sem build, sem deps).
   Envia POST /sinistros real (same-origin) e acompanha o ciclo via GET /casos/{id}.
   Nunca exibe veredito: o melhor resultado é "aceito para análise humana".
   ========================================================================== */
"use strict";

const POLL_INTERVALO_MS = 1000;
const POLL_TIMEOUT_MS = 20000;

const $ = (id) => document.getElementById(id);
const estado = { ultimoIdEnviado: null, historico: [], pollTimer: null };

/* ---------- Tema ---------- */
(function initTema() {
  const salvo = localStorage.getItem("console-tema");
  if (salvo) document.documentElement.setAttribute("data-theme", salvo);
  $("btn-tema").addEventListener("click", () => {
    const atual = document.documentElement.getAttribute("data-theme");
    const escuro = atual ? atual === "dark" : matchMedia("(prefers-color-scheme: dark)").matches;
    const novo = escuro ? "light" : "dark";
    document.documentElement.setAttribute("data-theme", novo);
    localStorage.setItem("console-tema", novo);
  });
})();

/* ---------- Cenários ---------- */
function idFresco() {
  const r = (crypto.randomUUID?.() || String(Math.random()).slice(2)).replace(/-/g, "").slice(0, 8);
  return "SIN-" + r.toUpperCase();
}

const CENARIOS = [
  {
    chave: "novo", rotulo: "Completo e novo",
    campos: () => ({
      idSinistro: idFresco(), apolice: "AP-1001",
      imei: "356789101112131", numeroSerie: "SN-42",
      fotos: "img://repo/frente\nimg://repo/nota",
      abertoEm: new Date().toISOString(), canal: "app", idCliente: "CLI-9",
    }),
  },
  {
    chave: "duplicado", rotulo: "Reenvio duplicado",
    campos: () => {
      const dica = estado.ultimoIdEnviado ? "" : "Envie o cenário “completo e novo” primeiro para ter um id a duplicar.";
      return {
        idSinistro: estado.ultimoIdEnviado || "SIN-EXEMPLO", apolice: "AP-1001",
        imei: "356789101112131", numeroSerie: "SN-42",
        fotos: "img://repo/frente\nimg://repo/nota",
        abertoEm: new Date().toISOString(), canal: "app", idCliente: "CLI-9",
        _dica: dica,
      };
    },
  },
  {
    chave: "parcial", rotulo: "Payload parcial",
    campos: () => ({ idSinistro: idFresco(), _dica: "Só o idSinistro — os demais campos ausentes marcam o caso como payload parcial (fail-open, não rejeita)." }),
  },
  {
    chave: "sem-id", rotulo: "Sem idSinistro",
    campos: () => ({ apolice: "AP-1001", fotos: "img://repo/frente", _dica: "Sem idSinistro o evento é não-processável → fila de erro técnico (202). O sinistro já existe no sistema principal." }),
  },
  {
    chave: "ilegivel", rotulo: "Corpo ilegível", alerta: true, raw: "{ isto não é json",
    campos: () => ({ _dica: "Este cenário envia um corpo inválido de propósito, para demonstrar a única rejeição de formato (400) — não é decisão de fraude." }),
  },
];

let cenarioAtivo = null;

function renderCenarios() {
  const box = $("cenarios");
  CENARIOS.forEach((c) => {
    const b = document.createElement("button");
    b.type = "button";
    b.className = "chip" + (c.alerta ? " chip-alerta" : "");
    b.textContent = c.rotulo;
    b.setAttribute("aria-pressed", "false");
    b.addEventListener("click", () => aplicarCenario(c, b));
    box.appendChild(b);
  });
}

function aplicarCenario(c, botao) {
  cenarioAtivo = c;
  document.querySelectorAll("#cenarios .chip").forEach((el) => el.setAttribute("aria-pressed", "false"));
  botao.setAttribute("aria-pressed", "true");

  const campos = c.campos();
  const setar = (id, v) => { $(id).value = v || ""; };
  setar("idSinistro", campos.idSinistro);
  setar("apolice", campos.apolice);
  setar("imei", campos.imei);
  setar("numeroSerie", campos.numeroSerie);
  setar("fotos", campos.fotos);
  setar("abertoEm", campos.abertoEm);
  setar("canal", campos.canal);
  setar("idCliente", campos.idCliente);

  const dica = $("dica-cenario");
  if (campos._dica) { dica.textContent = campos._dica; dica.classList.remove("oculto"); }
  else { dica.classList.add("oculto"); }
}

/* ---------- Montagem do payload ---------- */
function montarPayload() {
  const v = (id) => $(id).value.trim();
  const p = {};
  if (v("idSinistro")) p.idSinistro = v("idSinistro");
  if (v("apolice")) p.apolice = v("apolice");
  if (v("imei") || v("numeroSerie")) p.aparelho = { imei: v("imei") || null, numeroSerie: v("numeroSerie") || null };
  const fotos = v("fotos").split(/[\n,]/).map((s) => s.trim()).filter(Boolean);
  if (fotos.length) p.fotos = fotos;
  if (v("abertoEm") || v("canal") || v("idCliente")) {
    p.metadados = { abertoEm: v("abertoEm") || null, canal: v("canal") || null, idCliente: v("idCliente") || null };
  }
  return p;
}

/* ---------- Envio ---------- */
$("form-sinistro").addEventListener("submit", async (e) => {
  e.preventDefault();
  const ilegivel = cenarioAtivo?.raw !== undefined;
  const corpo = ilegivel ? cenarioAtivo.raw : JSON.stringify(montarPayload());
  const idSinistroEnviado = ilegivel ? null : (montarPayload().idSinistro || null);

  pararPolling();
  $("btn-enviar").disabled = true;
  $("vazio").classList.add("oculto");
  $("resultado").classList.remove("oculto");
  setCru("cru-req", `POST /sinistros\ncontent-type: application/json\n\n${corpo}`);
  setCru("cru-post", "");
  setCru("cru-get", "");

  try {
    const resp = await fetch("/sinistros", {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: corpo,
    });
    const texto = await resp.text();
    setCru("cru-post", `HTTP ${resp.status} ${resp.statusText}\n\n${texto || "(sem corpo)"}`);

    let caseId = null;
    try { caseId = JSON.parse(texto)?.caseId ?? null; } catch { /* corpo pode não ser JSON */ }

    comprovante(resp.status, caseId);
    resetStepper();

    if (resp.status === 202 && caseId) {
      if (idSinistroEnviado) estado.ultimoIdEnviado = idSinistroEnviado;
      registrarHistorico(caseId, idSinistroEnviado);
      marcarEtapa("recebido", "done", "202 aceito");
      marcarEtapa("ingestao", "active");
      iniciarPolling(caseId);
    } else {
      // 400 (ilegível) ou 503 (broker) — não há caso a acompanhar.
      marcarEtapa("recebido", "erro", resp.status === 400 ? "400 formato" : `${resp.status}`);
      $("traco-mensagem").textContent = resp.status === 400
        ? "Corpo ilegível — a única rejeição de formato. Não é decisão de fraude."
        : "Broker indisponível (503) — fronteira do fail-open. O sinistro segue no sistema principal.";
    }
  } catch (err) {
    comprovante(0, null);
    marcarEtapa("recebido", "erro", "sem rede");
    $("traco-mensagem").textContent = "Falha de rede ao chamar a API.";
  } finally {
    $("btn-enviar").disabled = false;
  }
});

/* ---------- Comprovante ---------- */
function comprovante(status, caseId) {
  const pill = $("comprovante-status");
  const titulo = $("comprovante-titulo");
  let tom = "warn", rotulo = String(status), frase = "—";
  if (status === 202) { tom = "ok"; rotulo = "202"; frase = "Recebido — aceito para análise humana"; }
  else if (status === 400) { tom = "crit"; rotulo = "400"; frase = "Corpo ilegível — rejeição de formato"; }
  else if (status === 503) { tom = "crit"; rotulo = "503"; frase = "Broker indisponível"; }
  else if (status === 0) { tom = "crit"; rotulo = "erro"; frase = "Sem resposta da API"; }
  pill.textContent = rotulo; pill.setAttribute("data-tom", tom);
  titulo.textContent = frase;

  $("cv-caseid").textContent = caseId || "—";
  $("cv-idem").textContent = "—";
  $("cv-destino").textContent = "—";
  $("cv-parcial").textContent = "—";
  $("cartao-caso").classList.add("oculto");
}

/* ---------- Stepper (traço de sinal) ---------- */
const ETAPAS = ["recebido", "ingestao", "worker", "caso"];
function resetStepper() {
  ETAPAS.forEach((e) => marcarEtapa(e, "", ""));
  $("traco-mensagem").textContent = "";
}
function marcarEtapa(etapa, estadoNovo, detalhe) {
  const li = document.querySelector(`.etapa[data-etapa="${etapa}"]`);
  if (!li) return;
  if (estadoNovo === "") li.removeAttribute("data-estado");
  else li.setAttribute("data-estado", estadoNovo);
  if (detalhe !== undefined) li.querySelector(".etapa-detalhe").textContent = detalhe || "";
}

/* ---------- Polling ---------- */
function pararPolling() { if (estado.pollTimer) { clearTimeout(estado.pollTimer); estado.pollTimer = null; } }

function iniciarPolling(caseId) {
  const deadline = Date.now() + POLL_TIMEOUT_MS;

  const tick = async () => {
    let dados = null;
    try {
      const resp = await fetch(`/casos/${caseId}`);
      const texto = await resp.text();
      setCru("cru-get", `GET /casos/${caseId}\nHTTP ${resp.status}\n\n${texto || "(sem corpo)"}`);
      if (resp.ok) { try { dados = JSON.parse(texto); } catch { /* ignore */ } }
    } catch { /* rede — tenta de novo até o timeout */ }

    if (dados) {
      const ing = dados.ingestao && dados.ingestao[0];
      if (ing) {
        $("cv-idem").textContent = legivelIdem(ing.idempotencia);
        $("cv-destino").textContent = legivelDestino(ing.destino);
        $("cv-parcial").textContent = ing.payloadParcial ? "parcial" : "completo";
        marcarEtapa("ingestao", "done", legivelDestino(ing.destino));

        // Ramos terminais: para cedo, não espera um caso que não virá.
        if (ing.destino === "Descartado") {
          marcarEtapa("worker", "terminal", "descartado");
          $("traco-mensagem").textContent = "Duplicado descartado por idempotência — nenhum novo caso é criado.";
          return pararPolling();
        }
        if (ing.destino === "FilaErroTecnico") {
          marcarEtapa("worker", "terminal", "erro técnico");
          $("traco-mensagem").textContent = "Roteado para a fila de erro técnico (não-processável). O sinistro já existe no sistema principal.";
          return pararPolling();
        }
      }

      if (dados.encontrado && dados.caso) {
        marcarEtapa("worker", "done", "processado");
        marcarEtapa("caso", "done", dados.caso.estado);
        $("traco-mensagem").textContent = "Ciclo concluído — caso encaminhado para análise humana.";
        renderCaso(dados);
        return pararPolling();
      }
      marcarEtapa("worker", "active", "processando…");
    }

    if (Date.now() >= deadline) {
      marcarEtapa("worker", "terminal", "sem resposta");
      $("traco-mensagem").textContent = "Worker ainda processando — verifique os logs do worker.";
      return pararPolling();
    }
    estado.pollTimer = setTimeout(tick, POLL_INTERVALO_MS);
  };

  tick();
}

/* ---------- Caso + trilhas ---------- */
function renderCaso(dados) {
  const c = dados.caso;
  $("cartao-caso").classList.remove("oculto");
  $("caso-estado-valor").textContent = legivelEstado(c.estado);
  $("caso-faixa").textContent = c.faixa || "—";
  $("caso-rota").textContent = c.rota || "—";
  $("caso-score").textContent = (c.score ?? null) === null ? "— (fail-open)" : c.score;
  $("caso-provider").textContent = c.versaoProvider || "—";

  const trilhas = $("trilhas");
  trilhas.innerHTML = "";
  trilhas.appendChild(blocoTrilha("Trilha de ingestão", (dados.ingestao || []).map((a) =>
    `${fmtData(a.recebidoEm)} · ${legivelIdem(a.idempotencia)} · ${legivelDestino(a.destino)}`)));
  trilhas.appendChild(blocoTrilha("Trilha de decisão", (dados.auditoria || []).map((a) =>
    `${fmtData(a.carimbadoEm)} · ${a.ator || "worker"} · ${a.faixa}/${a.rota}${a.causa ? " · " + a.causa : ""}`), true));
}

function blocoTrilha(titulo, linhas, imutavel) {
  const div = document.createElement("div");
  div.className = "trilha";
  const h = document.createElement("h4");
  h.textContent = titulo;
  if (imutavel) { const s = document.createElement("span"); s.className = "selo-imutavel"; s.textContent = "imutável"; h.appendChild(s); }
  div.appendChild(h);
  if (!linhas.length) { const p = document.createElement("p"); p.className = "trilha-linha"; p.textContent = "—"; div.appendChild(p); }
  linhas.forEach((t) => { const p = document.createElement("p"); p.className = "trilha-linha"; p.textContent = t; div.appendChild(p); });
  return div;
}

/* ---------- Histórico da sessão ---------- */
function registrarHistorico(caseId, idSinistro) {
  estado.historico.unshift({ caseId, idSinistro });
  const ul = $("historico");
  ul.innerHTML = "";
  estado.historico.forEach((h) => {
    const li = document.createElement("li");
    const b = document.createElement("button");
    b.type = "button"; b.className = "historico-item";
    b.innerHTML = `<span class="h-id">${h.caseId.slice(0, 8)}</span><span class="h-tag">${h.idSinistro || "sem id"}</span>`;
    b.addEventListener("click", () => { pararPolling(); resetStepper(); marcarEtapa("recebido", "done", "reaberto"); marcarEtapa("ingestao", "active"); iniciarPolling(h.caseId); });
    li.appendChild(b);
    ul.appendChild(li);
  });
}

/* ---------- Health + ambiente ---------- */
async function checarSaude() {
  try {
    const resp = await fetch("/health");
    const dados = await resp.json();
    $("saude").setAttribute("data-estado", "online");
    $("saude").querySelector(".saude-texto").textContent = "online";
    $("badge-ambiente").textContent = "ambiente: " + (dados.ambiente || "—");
    $("aviso-offline").classList.add("oculto");
  } catch {
    $("saude").setAttribute("data-estado", "offline");
    $("saude").querySelector(".saude-texto").textContent = "offline";
    $("aviso-offline").classList.remove("oculto");
  }
}

/* ---------- Helpers de exibição ---------- */
function setCru(id, txt) { $(id).textContent = txt; }
function fmtData(iso) { if (!iso) return "—"; try { return new Date(iso).toLocaleString("pt-BR"); } catch { return iso; } }
function legivelEstado(e) { return e === "PendenteRevisaoManual" ? "Pendente de revisão manual" : e; }
function legivelIdem(i) {
  return { PrimeiraVez: "primeira vez", DuplicadoDescartado: "duplicado descartado", ChecagemIndisponivel: "checagem indisponível" }[i] || i || "—";
}
function legivelDestino(d) {
  return { FilaProcessamento: "fila de processamento", FilaErroTecnico: "fila de erro técnico", Descartado: "descartado" }[d] || d || "—";
}

/* ---------- Boot ---------- */
renderCenarios();
aplicarCenario(CENARIOS[0], document.querySelector("#cenarios .chip"));
checarSaude();
