// Push handlers for a service worker. Import this file from your own worker:
//   importScripts("/App_Plugins/BaryoDev.PostAlerts/push-sw.js");
// The package also serves a standalone worker at /baryo-post-alerts/sw.js for sites that have none.
(function () {
  "use strict";

  function parse(event) {
    if (!event.data) return {};
    try {
      return event.data.json() || {};
    } catch (e) {
      return { body: event.data.text() };
    }
  }

  // Only addresses on this site are opened, whatever the payload says.
  function sameOrigin(url) {
    try {
      var u = new URL(url, self.location.origin);
      return u.origin === self.location.origin ? u.href : self.location.origin + "/";
    } catch (e) {
      return self.location.origin + "/";
    }
  }

  self.addEventListener("push", function (event) {
    var data = parse(event);
    var title = data.title || "New post";
    var options = {
      body: data.body || "",
      tag: data.tag || undefined,
      data: { url: sameOrigin(data.url || "/") }
    };
    if (data.icon) options.icon = data.icon;
    event.waitUntil(self.registration.showNotification(title, options));
  });

  self.addEventListener("notificationclick", function (event) {
    event.notification.close();
    var url = (event.notification.data && event.notification.data.url) || self.location.origin + "/";
    event.waitUntil(
      self.clients.matchAll({ type: "window", includeUncontrolled: true }).then(function (windows) {
        for (var i = 0; i < windows.length; i++) {
          var w = windows[i];
          if (w.url === url && "focus" in w) return w.focus();
        }
        return self.clients.openWindow ? self.clients.openWindow(url) : undefined;
      })
    );
  });

  // The browser can drop a subscription and issue a new one. Send the new one to the server.
  self.addEventListener("pushsubscriptionchange", function (event) {
    var old = event.oldSubscription;
    var key = old && old.options && old.options.applicationServerKey;
    if (!key) return;
    event.waitUntil(
      self.registration.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: key }).then(function (sub) {
        return fetch("/baryo-post-alerts/push/subscription", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify(sub.toJSON())
        });
      })
    );
  });
})();
