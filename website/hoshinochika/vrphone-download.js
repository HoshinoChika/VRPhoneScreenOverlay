"use strict";

document.querySelectorAll("a.download-link[data-vrphone-download]").forEach(link => {
  let pending = false;
  link.addEventListener("click", async event => {
    event.preventDefault();
    if (pending) return;
    pending = true;
    const previousText = link.textContent;
    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), 8000);
    link.textContent = "准备中";
    link.setAttribute("aria-busy", "true");
    try {
      const response = await fetch("/vrphonescreen/api/v1/updates/manifest?channel=beta", {
        cache: "no-store", signal: controller.signal
      });
      if (!response.ok) throw new Error("unavailable");
      const manifest = await response.json();
      const bytes = Uint8Array.from(atob(manifest.payload), char => char.charCodeAt(0));
      const release = JSON.parse(new TextDecoder().decode(bytes));
      if (release.schemaVersion !== 1 || release.packageFormat !== "full-install-v1" ||
          release.channel !== "beta" || typeof release.version !== "string" || release.version.length > 64 ||
          !/^\d+\.\d+\.\d+(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?$/.test(release.version)) {
        throw new Error("invalid");
      }
      window.location.assign("/vrphonescreen/api/v1/updates/download/" + encodeURIComponent(release.version));
      link.title = "下载最新版 VRPhoneScreen Overlay";
      link.textContent = previousText;
    } catch {
      link.textContent = "重试下载";
      link.title = "暂时无法下载，请稍后重试";
    } finally {
      clearTimeout(timeout);
      link.removeAttribute("aria-busy");
      pending = false;
    }
  });
});
