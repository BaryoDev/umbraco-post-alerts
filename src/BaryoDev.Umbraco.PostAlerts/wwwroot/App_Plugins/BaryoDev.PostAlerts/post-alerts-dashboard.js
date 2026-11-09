// Plain custom element with no build step, same approach as the Comments dashboard.
const API = "/umbraco/management/api/v1/post-alerts";
const BASE = "/App_Plugins/BaryoDev.PostAlerts";

const Base = await import("@umbraco-cms/backoffice/element-api")
  .then(({ UmbElementMixin }) => UmbElementMixin(HTMLElement))
  .catch(() => HTMLElement);

function esc(value) {
  return String(value ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]);
}

const tokens = new WeakMap();

async function token(host) {
  if (!tokens.has(host)) {
    tokens.set(host, Promise.all([
      import("@umbraco-cms/backoffice/context-api"),
      import("@umbraco-cms/backoffice/auth"),
    ]).then(([{ UmbContextConsumer }, { UMB_AUTH_CONTEXT }]) => {
      const consumer = new UmbContextConsumer(host, UMB_AUTH_CONTEXT);
      const auth = consumer.asPromise();
      consumer.request();
      return auth;
    }).catch(() => null));
  }
  const auth = await tokens.get(host);
  return auth ? auth.getLatestToken() : null;
}

async function call(host, path, method = "GET") {
  const t = await token(host);
  const response = await fetch(API + path, {
    method,
    credentials: "same-origin",
    headers: t ? { Authorization: `Bearer ${t}` } : {},
  });
  let body = null;
  try { body = response.status === 204 ? null : await response.json(); } catch { body = null; }
  if (response.status === 401 || response.status === 403) throw new Error("You do not have access to this.");
  if (!response.ok) throw new Error(body?.error || `Request failed (${response.status}).`);
  return body;
}

class PostAlertsDashboard extends Base {
  #data = null;
  #error = "";
  #notice = "";
  #busy = false;

  connectedCallback() {
    super.connectedCallback?.();
    this.#load();
  }

  async #load() {
    this.#busy = true;
    this.#render();
    try {
      this.#data = await call(this, "/summary");
      this.#error = "";
    } catch (e) {
      this.#error = e.message;
    }
    this.#busy = false;
    this.#render();
  }

  async #test() {
    this.#busy = true;
    this.#notice = "";
    this.#error = "";
    this.#render();
    try {
      const r = await call(this, "/test-digest", "POST");
      this.#notice = `Test digest with ${r.posts} post${r.posts === 1 ? "" : "s"} sent to ${r.sentTo}.`;
    } catch (e) {
      this.#error = e.message;
    }
    this.#busy = false;
    this.#render();
  }

  #render() {
    const d = this.#data;
    const when = (v) => (v ? new Date(v).toLocaleString() : "Not yet");
    const stat = (label, value, sub = "") => `
      <div class="bdpa-stat">
        <p class="bdpa-stat__label">${esc(label)}</p>
        <p class="bdpa-stat__value">${esc(value)}</p>
        ${sub ? `<p class="bdpa-stat__sub">${esc(sub)}</p>` : ""}
      </div>`;
    const problems = (title, list) => list?.length
      ? `<p class="bdpa-err">${esc(title)}<ul class="bdpa-problems">${list.map((p) => `<li>${esc(p)}</li>`).join("")}</ul></p>`
      : "";
    this.innerHTML = `
      <link rel="stylesheet" href="${BASE}/brand.css" />
      <div class="bdpa-admin">
        <div class="bdpa-admin__head">
          <img src="${BASE}/logo.svg" alt="baryo.dev" />
          <span>Post alerts</span>
        </div>
        ${this.#error ? `<p class="bdpa-err">${esc(this.#error)}</p>` : ""}
        ${this.#notice ? `<p class="bdpa-ok" role="status">${esc(this.#notice)}</p>` : ""}
        ${d ? `
          <div class="bdpa-stats">
            ${stat("Email subscribers", d.confirmedSubscribers, `${d.pendingSubscribers} waiting to confirm`)}
            ${stat("Browser push", d.pushSubscriptions, d.pushReady ? "Push is on" : "Push is off")}
            ${stat("Last digest", d.lastDigest ? when(d.lastDigest.completedUtc ?? d.lastDigest.periodEndUtc) : "Not yet",
              d.lastDigest ? `${d.lastDigest.status}, ${d.lastDigest.sent} of ${d.lastDigest.recipients} sent, ${d.lastDigest.posts} posts` : "")}
            ${stat("Next digest due", d.nextDigestUtc ? when(d.nextDigestUtc) : "Off", d.emailReady ? `Time zone ${d.timeZone}` : "Email is off")}
          </div>
          ${d.emailReady ? "" : problems("Email digest is off:", d.emailProblems)}
          ${d.pushReady ? "" : problems("Browser push is off:", d.pushProblems)}
        ` : `<p class="bdpa-note">${this.#busy ? "Loading." : ""}</p>`}
        <div class="bdpa-actions">
          <button class="bdpa-btn primary" data-test ${this.#busy || !d?.emailReady ? "disabled" : ""}>Send test digest to me</button>
          <button class="bdpa-btn" data-refresh ${this.#busy ? "disabled" : ""}>Refresh</button>
        </div>
        <p class="bdpa-note">The test email goes to your own backoffice address. It does not count as a digest run.</p>
      </div>`;
    this.querySelector("[data-test]")?.addEventListener("click", () => this.#test());
    this.querySelector("[data-refresh]")?.addEventListener("click", () => this.#load());
  }
}

customElements.define("baryodev-post-alerts-dashboard", PostAlertsDashboard);
export default PostAlertsDashboard;
export { PostAlertsDashboard as element };
