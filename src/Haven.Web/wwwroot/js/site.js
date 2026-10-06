(() => {
  "use strict";
  const dialog = document.getElementById("modal");
  const content = document.getElementById("modal-content");
  const message = document.getElementById("client-message");
  const headers = { "X-Requested-With": "XMLHttpRequest" };
  let opener;
  const announce = text => { message.textContent = text; message.hidden = false; };
  const focusForm = () => content.querySelector("input:not([type=hidden]), select, textarea, button")?.focus();
  const check = response => {
    if (response.status === 401) throw new Error("Your session expired. Sign in again and reload this page.");
    if (response.status === 403) throw new Error("This action is no longer allowed. Reload the page.");
    if (!response.ok) throw new Error("We couldn't complete this request. Reload the page and try again.");
  };
  document.addEventListener("click", async event => {
    const close = event.target.closest("[data-close-modal]");
    if (close) { dialog.close(); return; }
    const trigger = event.target.closest("[data-modal-url]");
    if (!trigger || trigger.disabled) return;
    event.preventDefault(); trigger.disabled = true; opener = trigger; message.hidden = true;
    try {
      const response = await fetch(trigger.dataset.modalUrl, { headers }); check(response);
      content.innerHTML = await response.text(); dialog.showModal(); focusForm();
    } catch (error) { announce(error.message); }
    finally { trigger.disabled = false; }
  });
  dialog.addEventListener("close", () => opener?.isConnected && opener.focus());
  document.addEventListener("submit", async event => {
    const form = event.target;
    if (form.dataset.confirm && !window.confirm(form.dataset.confirm)) { event.preventDefault(); return; }
    if (!form.matches("[data-modal-form]")) return;
    event.preventDefault();
    if (form.dataset.busy === "true") return;
    const data = new FormData(form); // Capture before disabling the controls.
    form.dataset.busy = "true";
    const buttons = [...form.querySelectorAll("button")]; buttons.forEach(b => b.disabled = true);
    try {
      const response = await fetch(form.action, { method: "POST", body: data, headers }); check(response);
      if (response.headers.get("content-type")?.includes("application/json")) {
        const result = await response.json();
        if (!result.success) throw new Error("The operation did not complete.");
        dialog.close();
        const region = document.querySelector("[data-refresh-root]");
        if (region) {
          const refreshed = await fetch(region.dataset.refreshUrl, { headers }); check(refreshed);
          region.innerHTML = await refreshed.text();
        }
      } else {
        content.innerHTML = await response.text();
        content.querySelector(".input-validation-error")?.focus();
      }
    } catch (error) { announce(error.message); }
    finally { buttons.forEach(b => b.disabled = false); delete form.dataset.busy; }
  });
})();
