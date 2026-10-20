let queueChart = null;
let chartRefreshInFlight = false;

const chartColors = [
    { border: "rgb(75, 192, 192)", background: "rgba(75, 192, 192, 0.2)" },  // Teal
    { border: "rgb(255, 99, 132)", background: "rgba(255, 99, 132, 0.2)" },  // Red
    { border: "rgb(54, 162, 235)", background: "rgba(54, 162, 235, 0.2)" },  // Blue
    { border: "rgb(255, 159, 64)", background: "rgba(255, 159, 64, 0.2)" },  // Orange
    { border: "rgb(153, 102, 255)", background: "rgba(153, 102, 255, 0.2)" }, // Purple
    { border: "rgb(201, 203, 207)", background: "rgba(201, 203, 207, 0.2)" }  // Gray
];

async function loadChartData() {
    const response = await fetch(window.chartDataUrl, { headers: { 'Accept': 'application/json' } });
    if (!response.ok) {
        console.error("Failed to load chart data:", response.status, response.statusText);
        return null;
    }
    return await response.json();
}

// Chart.js draws on a canvas, so it does not pick up the page's CSS: hand it the theme's colors.
function applyChartTheme() {
    const styles = getComputedStyle(document.documentElement);
    Chart.defaults.color = styles.getPropertyValue("--bs-secondary-color").trim() || "#666";
    Chart.defaults.borderColor = styles.getPropertyValue("--bs-border-color").trim() || "rgba(0, 0, 0, 0.1)";
}

function buildDataset(s, i) {
    const color = chartColors[i % chartColors.length];
    return {
        label: s.label,
        data: s.values,
        yAxisID: s.yAxisId || "y", // Must be set when the chart is built, otherwise a series starts on the wrong axis and jumps on the first refresh.
        borderColor: color.border,
        backgroundColor: color.background,
        borderWidth: 2,
        cubicInterpolationMode: "monotone", // Smooths the line without overshooting (dipping below zero or past the real peak).
        fill: true,
        pointRadius: 0,
        pointHoverRadius: 0,
        hitRadius: 6
    };
}

async function buildOrUpdateChart() {
    // Skip while a previous refresh is still running (so a slow response cannot land after a newer one) or while the tab is not visible.
    if (chartRefreshInFlight || document.hidden) return;
    chartRefreshInFlight = true;

    try {
        const data = await loadChartData();
        if (!data) return;

        const labels = data.labels || [];
        const series = data.series || [];

        const canvas = document.getElementById("queueChart");
        if (!canvas) {
            console.warn("Canvas #queueChart not found.");
            return;
        }

        // If no chart yet, or series count changed (e.g. history was empty), (re)build the chart.
        if (!queueChart || queueChart.data.datasets.length !== series.length) {
            if (queueChart) {
                queueChart.destroy();
                queueChart = null;
            }

            applyChartTheme();
            queueChart = new Chart(canvas.getContext("2d"), {
                type: "line",
                data: {
                    labels: labels,
                    datasets: series.map(buildDataset)
                },
                options: {
                    responsive: true,
                    interaction: {
                        mode: "index",
                        intersect: false
                    },
                    // The data window slides as old points drop off, so animating each refresh makes the whole line wobble.
                    animation: false,
                    plugins: {
                        tooltip: {
                            callbacks: {
                                label: function (context) {
                                    const value = context.parsed.y;
                                    const formatted = Number.isInteger(value)
                                        ? value.toLocaleString()
                                        : value.toLocaleString(undefined, { maximumFractionDigits: 2 });
                                    return `${context.dataset.label}: ${formatted}`;
                                }
                            }
                        }
                    },
                    scales: {
                        y: {
                            type: "linear",
                            position: "left",
                            beginAtZero: true
                        },
                        y1: {
                            type: "linear",
                            position: "right",
                            beginAtZero: true,
                            ticks: { precision: 0 }, // Queue depth is a whole number.
                            grid: { drawOnChartArea: false } // don't double-draw grid
                        }
                    }
                }
            });

            return;
        }

        // Otherwise, update existing chart in-place.
        queueChart.data.labels = labels;

        series.forEach((s, index) => {
            const ds = queueChart.data.datasets[index];
            ds.label = s.label;
            ds.data = s.values;
            ds.yAxisID = s.yAxisId || "y";
        });

        queueChart.update("none");
    } catch (err) {
        console.error("Error updating chart:", err);
    } finally {
        chartRefreshInFlight = false;
    }
}

document.addEventListener("DOMContentLoaded", async function () {
    await buildOrUpdateChart();
    setInterval(buildOrUpdateChart, 5000);

    // Rebuild the chart with the new colors when the theme changes.
    document.addEventListener("themechange", function () {
        if (queueChart) {
            queueChart.destroy();
            queueChart = null;
        }
        buildOrUpdateChart();
    });

    // Catch up right away when the tab becomes visible again.
    document.addEventListener("visibilitychange", function () {
        if (!document.hidden) buildOrUpdateChart();
    });
});
