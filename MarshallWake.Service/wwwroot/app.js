const state = { page: 1, pageSize: 20, total: 0 };
const $ = (id) => document.getElementById(id);

function formatTime(value) {
  return value ? new Date(value).toLocaleString() : "尚未执行";
}

async function request(url, options) {
  const response = await fetch(url, {
    headers: { "Content-Type": "application/json" },
    ...options
  });
  if (!response.ok) {
    const text = await response.text();
    throw new Error(text || `请求失败 (${response.status})`);
  }
  return response.json();
}

function toast(message) {
  $("toast").textContent = message;
  $("toast").classList.add("show");
  setTimeout(() => $("toast").classList.remove("show"), 3200);
}

async function loadStatus() {
  const status = await request("/api/status");
  $("service-state").textContent = status.isRunning
    ? "正在执行"
    : status.enabled ? "自动唤醒已开启" : "自动唤醒已关闭";
  $("service-state").classList.toggle("active", status.enabled);
  $("interval").textContent = `${status.intervalMinutes} 分钟`;
  $("last-scan").textContent = formatTime(status.lastScanAt);
  $("last-result").textContent = status.latest
    ? `${status.latest.succeeded ? "成功" : "失败"} · ${formatTime(status.latest.attemptedAt)}`
    : "尚未执行";
}

async function loadDevices() {
  const devices = await request("/api/devices");
  const container = $("devices");
  if (!devices.length) {
    container.innerHTML = '<p class="empty">尚未发现 Marshall 设备，请确认蓝牙已开启并点击“重新扫描”。</p>';
    return;
  }

  container.innerHTML = devices.map(device => `
    <article class="device">
      <div>
        <h3>${escapeHtml(device.name)}</h3>
        <p>${escapeHtml(device.id)} · ${device.isPaired ? "已配对" : "未配对"} · ${formatTime(device.lastSeenAt)}</p>
      </div>
      <button data-wake="${encodeURIComponent(device.id)}">立即唤醒</button>
    </article>
  `).join("");

  container.querySelectorAll("[data-wake]").forEach(button => {
    button.addEventListener("click", () => wakeDevice(button));
  });
}

async function loadHistory() {
  const filter = $("result-filter").value;
  const query = new URLSearchParams({
    page: state.page,
    pageSize: state.pageSize
  });
  if (filter) query.set("succeeded", filter);

  const result = await request(`/api/history?${query}`);
  state.total = result.total;
  $("history").innerHTML = result.items.length
    ? result.items.map(item => `
      <tr>
        <td>${formatTime(item.attemptedAt)}</td>
        <td>${escapeHtml(item.deviceName)}</td>
        <td>${item.trigger === "scheduled" ? "定时" : "手动"}</td>
        <td><span class="status ${item.succeeded ? "success" : "failure"}">${item.succeeded ? "成功" : "失败"}</span></td>
        <td>${escapeHtml(item.message)} · ${item.durationMilliseconds} ms</td>
      </tr>
    `).join("")
    : '<tr><td colspan="5" class="empty">暂无执行记录</td></tr>';

  const pages = Math.max(1, Math.ceil(result.total / result.pageSize));
  $("page-info").textContent = `第 ${result.page} / ${pages} 页，共 ${result.total} 条`;
  $("previous").disabled = state.page <= 1;
  $("next").disabled = state.page >= pages;
}

async function wakeDevice(button) {
  button.disabled = true;
  try {
    const deviceId = decodeURIComponent(button.dataset.wake);
    const attempts = await request("/api/wake", {
      method: "POST",
      body: JSON.stringify({ deviceId })
    });
    const attempt = attempts[0];
    toast(`${attempt.deviceName}: ${attempt.succeeded ? "唤醒成功" : attempt.message}`);
    await Promise.all([loadStatus(), loadHistory()]);
  } catch (error) {
    toast(error.message);
  } finally {
    button.disabled = false;
  }
}

function escapeHtml(value) {
  const element = document.createElement("span");
  element.textContent = value ?? "";
  return element.innerHTML;
}

$("refresh").addEventListener("click", async event => {
  event.currentTarget.disabled = true;
  try {
    const devices = await request("/api/devices/refresh", { method: "POST" });
    toast(`发现 ${devices.length} 台 Marshall 设备`);
    await Promise.all([loadDevices(), loadStatus()]);
  } catch (error) {
    toast(error.message);
  } finally {
    event.currentTarget.disabled = false;
  }
});

$("result-filter").addEventListener("change", () => {
  state.page = 1;
  loadHistory().catch(error => toast(error.message));
});
$("previous").addEventListener("click", () => {
  state.page--;
  loadHistory().catch(error => toast(error.message));
});
$("next").addEventListener("click", () => {
  state.page++;
  loadHistory().catch(error => toast(error.message));
});

Promise.all([loadStatus(), loadDevices(), loadHistory()])
  .catch(error => toast(error.message));
setInterval(() => Promise.all([loadStatus(), loadDevices()]), 15000);
