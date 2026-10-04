(function () {
  var data = window.AXUM_HOME;
  if (!data) return;

  function esc(value) {
    return String(value)
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;")
      .replace(/"/g, "&quot;");
  }

  var byPair = {};
  data.forecastAll.forEach(function (row) {
    var key = row.sku + "||" + row.region;
    if (!byPair[key]) byPair[key] = {};
    byPair[key]["day" + row.day_offset] = row;
  });

  var months = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

  function fmtNum(value) {
    return value.toLocaleString("en-US", { maximumFractionDigits: 2 });
  }

  function prettyDate(iso) {
    var parts = String(iso).split("-");
    return Number(parts[2]) + " " + months[Number(parts[1]) - 1] + " " + parts[0];
  }

  var spanInfo = (function () {
    var min = Infinity;
    var max = -Infinity;
    data.forecastAll.forEach(function (row) {
      if (row.forecast_units < min) min = row.forecast_units;
      if (row.forecast_units > max) max = row.forecast_units;
    });
    var span = max - min;
    return { min: min, max: max, cut1: min + span / 3, cut2: min + (2 * span) / 3 };
  })();

  function newestHistoryDate() {
    var newest = "";
    var signals = data.historySignal || {};
    Object.keys(signals).forEach(function (key) {
      var last = signals[key].last_date || "";
      if (last > newest) newest = last;
    });
    return newest;
  }

  function pairPoint(pair) {
    var values = [];
    if (pair.day1) values.push(pair.day1.forecast_units);
    if (pair.day2) values.push(pair.day2.forecast_units);
    if (!values.length) return null;
    var value = values[0];
    for (var i = 1; i < values.length; i += 1) {
      if (values[i] > value) value = values[i];
    }
    return value;
  }

  function thirdOf(value) {
    if (value < spanInfo.cut1) return "Low";
    if (value < spanInfo.cut2) return "Medium";
    return "High";
  }

  function holdReason(key) {
    var signal = data.historySignal && data.historySignal[key];
    var newest = newestHistoryDate();
    if (!signal) return "No order history is stored for this row, so it cannot sit in High.";
    if (signal.last_date < newest) {
      return "Order history stopped on " + prettyDate(signal.last_date) + ", before the newest day in the sample (" + prettyDate(newest) + ").";
    }
    if (signal.typical_miss > signal.mean_units) {
      return "Past days swing by about " + fmtNum(signal.typical_miss) + " units, more than their average of " + fmtNum(signal.mean_units) + ".";
    }
    return "";
  }

  function assessPair(pair, key) {
    var value = pairPoint(pair);
    if (value === null) return { label: "n/a", third: "n/a", held: false, reason: "" };
    var third = thirdOf(value);
    var reason = holdReason(key);
    var held = third === "High" && reason !== "";
    return { label: held ? "Medium" : third, third: third, held: held, reason: held ? reason : "" };
  }

  var rowsData = Object.keys(byPair).map(function (key) {
    var parts = key.split("||");
    var pair = byPair[key];
    var assessed = assessPair(pair, key);
    return {
      sku: parts[0],
      region: parts[1],
      key: key,
      d1: pair.day1 ? pair.day1.forecast_units : null,
      d1_low: pair.day1 ? pair.day1.low : null,
      d1_high: pair.day1 ? pair.day1.high : null,
      d2: pair.day2 ? pair.day2.forecast_units : null,
      d2_low: pair.day2 ? pair.day2.low : null,
      d2_high: pair.day2 ? pair.day2.high : null,
      conf: assessed.label,
      held: assessed.held,
      reason: assessed.reason
    };
  });

  var sortKey = "d1";
  var sortDir = -1;
  var currentPage = 1;
  var pageSize = 20;

  function getFiltered() {
    var region = document.getElementById("regionFilter").value;
    var sku = document.getElementById("skuFilter").value;
    var conf = document.getElementById("confFilter").value;
    var q = document.getElementById("searchBox").value.trim().toLowerCase();
    return rowsData.filter(function (row) {
      if (region && row.region !== region) return false;
      if (sku && row.sku !== sku) return false;
      if (conf && row.conf !== conf) return false;
      if (q && row.sku.toLowerCase().indexOf(q) === -1 && row.region.toLowerCase().indexOf(q) === -1) return false;
      return true;
    });
  }

  function rangeText(low, high, has) {
    if (!has) return "";
    return low.toLocaleString() + " to " + high.toLocaleString();
  }

  function renderTable() {
    var rows = getFiltered();
    rows.sort(function (a, b) {
      var av = a[sortKey];
      var bv = b[sortKey];
      if (typeof av === "string") {
        av = av.toLowerCase();
        bv = String(bv).toLowerCase();
      }
      if (av === null) av = -Infinity;
      if (bv === null) bv = -Infinity;
      if (av < bv) return -1 * sortDir;
      if (av > bv) return 1 * sortDir;
      return 0;
    });
    var totalPages = Math.max(1, Math.ceil(rows.length / pageSize));
    currentPage = Math.min(currentPage, totalPages);
    var pageRows = rows.slice((currentPage - 1) * pageSize, currentPage * pageSize);
    document.getElementById("forecastBody").innerHTML = pageRows.map(function (row) {
      return '<tr class="' + (row.conf === "Low" ? "is-low" : "") + '" data-key="' + esc(row.key) + '">' +
        '<td><span class="sku-open">' + esc(row.sku) + '<svg width="16" height="16" viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.6" aria-hidden="true"><path d="M6 3.5 10.5 8 6 12.5"/></svg></span></td>' +
        "<td>" + esc(row.region) + "</td>" +
        '<td class="num">' + (row.d1 !== null ? row.d1.toLocaleString() + (rangeText(row.d1_low, row.d1_high, true) ? " · " + rangeText(row.d1_low, row.d1_high, true) : "") : "None") + "</td>" +
        '<td class="num">' + (row.d2 !== null ? row.d2.toLocaleString() + (rangeText(row.d2_low, row.d2_high, true) ? " · " + rangeText(row.d2_low, row.d2_high, true) : "") : "None") + "</td>" +
        '<td class="conf"' + (row.reason ? ' title="' + esc(row.reason) + '"' : "") + ">" + esc(row.conf) +
        (row.held ? "<small>Held from the upper third</small>" : "") + "</td></tr>";
    }).join("");
    document.getElementById("rowCountLabel").textContent = rows.length + " combinations · page " + currentPage + " of " + totalPages;

    var pagHtml = "";
    for (var p = 1; p <= totalPages; p += 1) {
      if (p === 1 || p === totalPages || Math.abs(p - currentPage) <= 2) {
        pagHtml += '<button type="button" class="btn btn-ghost' + (p === currentPage ? " is-on" : "") + '" data-page="' + p + '">' + p + "</button>";
      } else if (Math.abs(p - currentPage) === 3) {
        pagHtml += '<span class="meta" style="padding:0.4rem;">…</span>';
      }
    }
    var pag = document.getElementById("pagination");
    pag.innerHTML = pagHtml;
    pag.querySelectorAll("button[data-page]").forEach(function (button) {
      button.addEventListener("click", function () {
        currentPage = parseInt(button.dataset.page, 10);
        renderTable();
      });
    });
    document.querySelectorAll("#forecastBody tr").forEach(function (tr) {
      tr.addEventListener("click", function () { openDetail(tr.dataset.key); });
    });
  }

  document.querySelectorAll("th[data-sort]").forEach(function (th) {
    th.addEventListener("click", function () {
      var key = th.dataset.sort;
      if (sortKey === key) sortDir *= -1;
      else { sortKey = key; sortDir = key === "sku" || key === "region" || key === "conf" ? 1 : -1; }
      currentPage = 1;
      renderTable();
    });
  });
  ["regionFilter", "skuFilter", "confFilter"].forEach(function (id) {
    document.getElementById(id).addEventListener("change", function () { currentPage = 1; renderTable(); });
  });
  document.getElementById("searchBox").addEventListener("input", function () { currentPage = 1; renderTable(); });
  document.getElementById("exportBtn").addEventListener("click", function () {
    var rows = getFiltered();
    var header = "sku,region,day1_forecast,day1_low,day1_high,day2_forecast,day2_low,day2_high,confidence\n";
    function csvCell(value) {
      var text = value === null || value === undefined ? "" : String(value);
      if (/[",\n]/.test(text)) return '"' + text.replace(/"/g, '""') + '"';
      return text;
    }
    var body = rows.map(function (row) {
      return [row.sku, row.region, row.d1, row.d1_low, row.d1_high, row.d2, row.d2_low, row.d2_high, row.conf].map(csvCell).join(",");
    }).join("\n");
    var blob = new Blob([header + body], { type: "text/csv" });
    var a = document.createElement("a");
    a.href = URL.createObjectURL(blob);
    a.download = "axum_home_forecast_export.csv";
    a.click();
  });

  var regions = Array.from(new Set(rowsData.map(function (row) { return row.region; }))).sort();
  var skus = Array.from(new Set(rowsData.map(function (row) { return row.sku; }))).sort();
  regions.forEach(function (region) {
    document.getElementById("regionFilter").insertAdjacentHTML("beforeend", '<option value="' + esc(region) + '">' + esc(region) + "</option>");
  });
  skus.forEach(function (sku) {
    document.getElementById("skuFilter").insertAdjacentHTML("beforeend", '<option value="' + esc(sku) + '">' + esc(sku) + "</option>");
  });
  document.getElementById("modelCount").textContent = Object.keys(byPair).length + " SKU × region models";

  var newestDay = newestHistoryDate();
  document.getElementById("confidenceThirds").textContent =
    "Point forecasts in this sample run from " + fmtNum(spanInfo.min) + " to " + fmtNum(spanInfo.max) +
    ". The span from the lowest to the highest is split into three equal thirds. Below " + fmtNum(spanInfo.cut1) +
    " is Low. From " + fmtNum(spanInfo.cut1) + " and below " + fmtNum(spanInfo.cut2) +
    " is Medium. From " + fmtNum(spanInfo.cut2) + " through " + fmtNum(spanInfo.max) +
    " is High. A row uses its higher day.";
  document.getElementById("confidenceHistory").textContent =
    "A second check can hold a row out of High. The row stays High only when its order history reaches the newest day in the sample (" +
    prettyDate(newestDay) + ") and the past days swing less than their own average. If the history stopped earlier, or the days swing more than that average, the label drops to Medium and the range is drawn wider. A fresh series that passes this check stays where the third puts it.";
  document.querySelector('th[data-sort="conf"]').title =
    "Three equal thirds of the sample span, from " + fmtNum(spanInfo.min) + " to " + fmtNum(spanInfo.max) +
    ". History can hold a row out of High.";

  renderTable();

  var attention = rowsData.filter(function (row) { return row.conf === "Low" && row.d1 !== null; })
    .sort(function (a, b) { return b.d1 - a.d1; })
    .slice(0, 12);
  document.getElementById("attentionBody").innerHTML = attention.map(function (row) {
    return '<tr data-key="' + esc(row.key) + '"><td>' + esc(row.sku) + "</td><td>" + esc(row.region) + '</td><td class="num">' + row.d1.toLocaleString() + '</td><td class="conf">' + esc(row.conf) + "</td></tr>";
  }).join("");
  document.querySelectorAll("#attentionBody tr").forEach(function (tr) {
    tr.addEventListener("click", function () { openDetail(tr.dataset.key); });
  });

  var totalNext2 = data.forecastAll.reduce(function (sum, row) { return sum + row.forecast_units; }, 0);
  document.getElementById("kpi-row").innerHTML =
    '<article class="paper kpi"><span>Forecasted units, next 2 days</span><strong>' + totalNext2.toLocaleString() + "</strong></article>" +
    '<article class="paper kpi"><span>SKUs covered</span><strong>' + skus.length + "</strong></article>" +
    '<article class="paper kpi"><span>Regions covered</span><strong>' + regions.length + "</strong></article>" +
    '<article class="paper kpi"><span>SKU × region models</span><strong>' + Object.keys(byPair).length + "</strong></article>";

  document.getElementById("skippedBody").innerHTML = data.skippedCombos.map(function (row) {
    return "<tr><td>" + esc(row.sku) + "</td><td>" + esc(row.region) + '</td><td class="num">' + row.days + '</td><td class="num">' + row.total_units.toLocaleString() + "</td></tr>";
  }).join("");

  function addDays(iso, days) {
    var parts = iso.split("-");
    var date = new Date(Date.UTC(Number(parts[0]), Number(parts[1]) - 1, Number(parts[2])));
    date.setUTCDate(date.getUTCDate() + days);
    return date.toISOString().slice(0, 10);
  }

  var modalChart = null;
  var charts = {};

  function chartDefaults() {
    if (typeof Chart === "undefined") return false;
    Chart.defaults.font.family = '"Source Sans 3", "Segoe UI", sans-serif';
    Chart.defaults.font.size = 12;
    Chart.defaults.color = "#57534E";
    return true;
  }

  function buildBandedChart(canvasId, history, forecast) {
    if (!chartDefaults()) return null;
    var labels = history.map(function (h) { return h.date; }).concat(forecast.map(function (f) { return f.date; }));
    var actualData = history.map(function (h) { return h.units; }).concat(forecast.map(function () { return null; }));
    var forecastData = history.map(function () { return null; }).concat(forecast.map(function (f) { return f.forecast; }));
    if (history.length && forecast.length) forecastData[history.length - 1] = history[history.length - 1].units;
    var bandHigh = history.map(function () { return null; }).concat(forecast.map(function (f) { return f.high; }));
    var bandLow = history.map(function () { return null; }).concat(forecast.map(function (f) { return f.low; }));
    return new Chart(document.getElementById(canvasId), {
      type: "line",
      data: {
        labels: labels,
        datasets: [
          { label: "Confidence range", data: bandHigh, borderColor: "transparent", backgroundColor: "rgba(28,25,23,0.1)", fill: "+1", pointRadius: 0, tension: 0.2 },
          { label: "_low", data: bandLow, borderColor: "transparent", backgroundColor: "transparent", fill: false, pointRadius: 0, tension: 0.2 },
          { label: "Actual", data: actualData, borderColor: "#1C1917", backgroundColor: "transparent", tension: 0.25, spanGaps: false, pointRadius: 2 },
          { label: "Forecast", data: forecastData, borderColor: "#57534E", borderDash: [5, 4], backgroundColor: "transparent", tension: 0.25, spanGaps: false, pointRadius: 2 }
        ]
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        plugins: { legend: { labels: { filter: function (item) { return item.text !== "_low"; }, boxWidth: 12 } } },
        scales: {
          x: { ticks: { maxTicksLimit: 8 }, grid: { display: false } },
          y: { grid: { color: "rgba(28,25,23,0.08)" } }
        }
      }
    });
  }

  function buildBarChart(canvasId, items, labelKey) {
    if (!chartDefaults() || charts[canvasId]) return;
    charts[canvasId] = new Chart(document.getElementById(canvasId), {
      type: "bar",
      data: {
        labels: items.map(function (item) {
          var value = String(item[labelKey]);
          return labelKey === "region" ? value : value.toUpperCase();
        }),
        datasets: [{ label: "Forecast units", data: items.map(function (item) { return item.units; }), backgroundColor: "#1C1917", borderRadius: 3 }]
      },
      options: {
        indexAxis: "y",
        responsive: true,
        maintainAspectRatio: false,
        plugins: { legend: { display: false } },
        scales: { x: { ticks: { font: { size: 11 } } }, y: { ticks: { font: { size: 11 } } } }
      }
    });
  }

  function showPanel(name) {
    document.querySelectorAll("[data-panel]").forEach(function (panel) {
      panel.classList.toggle("is-on", panel.getAttribute("data-panel") === name);
    });
    document.querySelectorAll(".rail button").forEach(function (tab) {
      tab.classList.toggle("is-on", tab.getAttribute("data-tab") === name);
    });
    if (name === "region") buildBarChart("regionChart", data.rollups.regions.slice(0, 15), "region");
    if (name === "sku") buildBarChart("skuChart", data.rollups.skus, "sku");
  }
  document.querySelectorAll(".rail button").forEach(function (tab) {
    tab.addEventListener("click", function () { showPanel(tab.getAttribute("data-tab")); });
  });

  buildBandedChart("workedChart", data.workedHistory, data.workedForecast);

  function presentDialog(modal) {
    var dialog = document.getElementById("detailDialog");
    var wantModal = modal !== false;
    if (dialog.open && dialog.matches(":modal") !== wantModal) dialog.close();
    if (!dialog.open) {
      if (wantModal) dialog.showModal();
      else dialog.show();
    }
  }

  function openDetail(key, modal) {
    var parts = key.split("||");
    var sku = parts[0];
    var region = parts[1];
    var pair = byPair[key];
    if (!pair) return;
    document.getElementById("modalTitle").textContent = sku + ", " + region;
    var assessed = assessPair(pair, key);
    var conf = assessed.label;
    function cell(label, value) {
      return '<article class="paper kpi"><span>' + label + "</span><strong>" + value + "</strong></article>";
    }
    document.getElementById("modalKpis").innerHTML =
      cell("Day 1", pair.day1 ? pair.day1.forecast_units.toLocaleString() : "None") +
      cell("Day 2", pair.day2 ? pair.day2.forecast_units.toLocaleString() : "None") +
      cell("Confidence", conf);
    if (modalChart) { modalChart.destroy(); modalChart = null; }
    var hist = data.historyByCombo[key];
    var forecastPts = null;
    if (key === "GC210 Glass cleaner lemon||Gauteng, Johannesburg Area") {
      hist = data.workedHistory;
      forecastPts = data.workedForecast;
    } else if (hist && hist.length) {
      var last = hist[hist.length - 1].date;
      forecastPts = [pair.day1, pair.day2].filter(Boolean).map(function (row, index) {
        return { date: addDays(last, index + 1), forecast: row.forecast_units, low: row.low, high: row.high };
      });
    }
    var wrap = document.getElementById("modalChartWrap");
    var note = document.getElementById("modalNote");
    if (hist && forecastPts) {
      wrap.hidden = false;
      note.textContent = (pair.day1 ? "Day 1 range " + pair.day1.low.toLocaleString() + " to " + pair.day1.high.toLocaleString() + (pair.day2 ? ". Day 2 range " + pair.day2.low.toLocaleString() + " to " + pair.day2.high.toLocaleString() + "." : ".") : "") + (assessed.held ? " " + assessed.reason + " The label drops to Medium, and the range is drawn wider." : "");
      presentDialog(modal);
      modalChart = buildBandedChart("modalChart", hist, forecastPts);
    } else {
      wrap.hidden = true;
      note.textContent = "Daily history chart not embedded for this pair in the demo. The forecast table still shows its forecast and range." + (assessed.held ? " " + assessed.reason + " The label drops to Medium, and the range is drawn wider." : "");
      presentDialog(modal);
    }
  }
  document.getElementById("closeDetail").addEventListener("click", function () {
    document.getElementById("detailDialog").close();
  });

  var steps = [
    {
      tab: "overview",
      sel: "#cleaned-orders",
      title: "Cleaned orders",
      text: "Glass cleaner lemon in Gauteng, Johannesburg Area. This chart starts from cleaned orders: the last 30 days of actual demand."
    },
    {
      tab: "explorer",
      sel: "#next-two-days",
      title: "The next two days",
      text: "Each product and region has a forecast for the next two days. The note above the table explains the two checks behind High, Medium, and Low."
    },
    {
      tab: "explorer",
      sel: "#modalNote",
      title: "The range",
      openKey: "GC210 Glass cleaner lemon||Gauteng, Johannesburg Area",
      text: "This row is open so you can see the range around those two days. The label follows three equal thirds of the sample. History can hold a row out of High when the series stopped early or the past days swing more than their own average."
    },
    {
      tab: "quality",
      sel: "#cleanup",
      title: "What was thrown out",
      text: "The data quality screen shows what was thrown out of the raw export, and the pairs that are not forecasted yet."
    }
  ];
  var ti = 0;
  var hl = null;
  var walk = document.getElementById("walk");

  function clearHL() {
    if (hl) hl.classList.remove("is-focus");
    hl = null;
  }
  function closeDetailQuiet() {
    var dialog = document.getElementById("detailDialog");
    if (dialog.open) dialog.close();
  }
  function parkWalk(inline) {
    if (inline) {
      document.querySelector("#detailDialog .sheet-body").appendChild(walk);
      walk.classList.add("is-inline");
    } else {
      document.body.appendChild(walk);
      walk.classList.remove("is-inline");
    }
  }
  function showWalk(inline) {
    parkWalk(inline);
    walk.hidden = false;
  }
  function hideWalk() {
    parkWalk(false);
    walk.hidden = true;
  }
  function showStep(i) {
    ti = i;
    var step = steps[i];
    if (step.openKey) {
      showPanel(step.tab);
      openDetail(step.openKey);
      showWalk(true);
    } else {
      showWalk(false);
      closeDetailQuiet();
      showPanel(step.tab);
    }
    clearHL();
    window.setTimeout(function () {
      var el = document.querySelector(step.sel);
      if (!el) return;
      el.classList.add("is-focus");
      hl = el;
      el.scrollIntoView({ behavior: "smooth", block: "center" });
    }, 60);
    document.getElementById("walk-count").textContent = (i + 1) + " of " + steps.length;
    document.getElementById("walk-title").textContent = step.title;
    document.getElementById("walk-text").textContent = step.text;
    document.getElementById("walk-back").disabled = i === 0;
    document.getElementById("walk-next").textContent = "Next";
  }
  function endWalk() {
    clearHL();
    closeDetailQuiet();
    hideWalk();
  }
  document.getElementById("start-walk").addEventListener("click", function () { showStep(0); });
  document.getElementById("look-around").addEventListener("click", function () {
    endWalk();
    showPanel("overview");
    document.querySelector(".rail").scrollIntoView({ block: "nearest" });
  });
  document.getElementById("walk-next").addEventListener("click", function () {
    if (ti < steps.length - 1) showStep(ti + 1);
    else endWalk();
  });
  document.getElementById("walk-back").addEventListener("click", function () {
    if (ti > 0) showStep(ti - 1);
  });
  document.getElementById("walk-end").addEventListener("click", endWalk);
})();
