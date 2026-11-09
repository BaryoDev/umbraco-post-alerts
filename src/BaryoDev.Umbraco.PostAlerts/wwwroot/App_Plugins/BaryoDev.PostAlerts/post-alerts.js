// Progressive enhancement. The email form works without this file.
(function () {
  "use strict";

  var MESSAGES = {
    sent: "Thanks. If the address is new, a confirmation email is on its way. Open the link in it to finish.",
    "error-invalid": "That email address does not look right.",
    "error-ratelimited": "Too many tries from your network. Try again later.",
    "error-antiforgery": "The form expired. Reload the page and send it again.",
    "error-unavailable": "Email sign-up is not available right now.",
    network: "Could not reach the site. Try again.",
    "push-on": "Done. This browser will get a notification for each new post.",
    "push-off": "Notifications are off for this browser.",
    "push-denied": "Notifications are blocked in this browser. Change the site permission to turn them on.",
    "push-failed": "Could not turn on notifications. Try again later."
  };

  function status(root, key, isError) {
    var el = root.querySelector("[data-bdpa-status]");
    if (!el) return;
    el.textContent = MESSAGES[key] || key;
    el.classList.toggle("bdpa__status--error", !!isError);
  }

  function base64UrlToBytes(value) {
    var padded = (value + "===".slice((value.length + 3) % 4)).replace(/-/g, "+").replace(/_/g, "/");
    var raw = atob(padded);
    var out = new Uint8Array(raw.length);
    for (var i = 0; i < raw.length; i++) out[i] = raw.charCodeAt(i);
    return out;
  }

  function setupEmail(root) {
    var form = root.querySelector("[data-bdpa-form]");
    if (!form || !window.fetch || !window.FormData) return;
    form.addEventListener("submit", function (event) {
      event.preventDefault();
      var button = form.querySelector("button[type=submit]");
      button.disabled = true;
      fetch(form.action, {
        method: "POST",
        body: new FormData(form),
        credentials: "same-origin",
        headers: { Accept: "application/json" }
      }).then(function (response) {
        return response.json().catch(function () { return {}; }).then(function (body) {
          var code = body.code || (response.ok ? "sent" : "error-unavailable");
          status(root, code, !response.ok);
          if (response.ok) form.reset();
        });
      }).catch(function () {
        // Fall back to the normal post, which always works.
        form.submit();
      }).then(function () {
        button.disabled = false;
      });
    });
  }

  function setupPush(root) {
    var box = root.querySelector("[data-bdpa-push]");
    var button = root.querySelector("[data-bdpa-push-button]");
    if (!box || !button) return;
    if (!("serviceWorker" in navigator) || !("PushManager" in window) || !("Notification" in window)) return;

    var keyUrl = root.getAttribute("data-key-url");
    var subUrl = root.getAttribute("data-subscription-url");
    var swUrl = root.getAttribute("data-sw-url");

    function registration() {
      return navigator.serviceWorker.getRegistration().then(function (existing) {
        return existing || navigator.serviceWorker.register(swUrl, { scope: "/" });
      }).then(function () {
        return navigator.serviceWorker.ready;
      });
    }

    function render(on) {
      button.setAttribute("aria-pressed", on ? "true" : "false");
      button.textContent = on ? "Stop notifications in this browser" : "Notify me in this browser";
    }

    // Show the block only after the checks above, and set the right label for this browser.
    navigator.serviceWorker.getRegistration().then(function (reg) {
      return reg ? reg.pushManager.getSubscription() : null;
    }).then(function (sub) {
      render(!!sub);
      box.hidden = false;
    }).catch(function () {
      render(false);
      box.hidden = false;
    });

    button.addEventListener("click", function () {
      button.disabled = true;
      var turningOn = button.getAttribute("aria-pressed") !== "true";
      var work = turningOn ? turnOn() : turnOff();
      work.catch(function () {
        status(root, "push-failed", true);
      }).then(function () {
        button.disabled = false;
      });
    });

    function turnOn() {
      // Permission is requested here, after the click, and nowhere else.
      return Notification.requestPermission().then(function (permission) {
        if (permission !== "granted") {
          status(root, "push-denied", true);
          return;
        }
        return registration().then(function (reg) {
          return fetch(keyUrl, { credentials: "same-origin" }).then(function (r) {
            if (!r.ok) throw new Error("key");
            return r.json();
          }).then(function (k) {
            return reg.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: base64UrlToBytes(k.publicKey) });
          });
        }).then(function (sub) {
          return fetch(subUrl, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(sub.toJSON())
          }).then(function (r) {
            if (!r.ok) {
              return sub.unsubscribe().then(function () { throw new Error("save"); });
            }
            render(true);
            status(root, "push-on", false);
          });
        });
      });
    }

    function turnOff() {
      return navigator.serviceWorker.getRegistration().then(function (reg) {
        return reg ? reg.pushManager.getSubscription() : null;
      }).then(function (sub) {
        if (!sub) { render(false); return; }
        var endpoint = sub.endpoint;
        return fetch(subUrl, {
          method: "DELETE",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ endpoint: endpoint })
        }).then(function () {
          return sub.unsubscribe();
        }).then(function () {
          render(false);
          status(root, "push-off", false);
        });
      });
    }
  }

  document.querySelectorAll("[data-bdpa]").forEach(function (root) {
    setupEmail(root);
    setupPush(root);
  });
})();
