let queueChart = null;

async function loadChartData() {
    const url = window.chartDataUrl;

    const response = await fetch(url);
    if (!response.ok) {
        console.error("Failed to load chart data:", response.status, response.statusText);
        return null;
    }
    return await response.json();
}

async function buildOrUpdateChart() {
    const data = await loadChartData();
    if (!data) return;

    const labels = data.labels || [];
    const series = data.series || [];

    const canvas = document.getElementById("queueChart");
    if (!canvas) {
        console.warn("Canvas #queueChart not found.");
        return;
    }

    const ctx = canvas.getContext("2d");

    const colors = [
        { border: "rgb(75, 192, 192)", background: "rgba(75, 192, 192, 0.2)" },  // Teal
        { border: "rgb(255, 99, 132)", background: "rgba(255, 99, 132, 0.2)" },  // Red
        { border: "rgb(54, 162, 235)", background: "rgba(54, 162, 235, 0.2)" },  // Blue

        { border: "rgb(255, 159, 64)", background: "rgba(255, 159, 64, 0.2)" },  // Orange
        { border: "rgb(153, 102, 255)", background: "rgba(153, 102, 255, 0.2)" }, // Purple
        { border: "rgb(201, 203, 207)", background: "rgba(201, 203, 207, 0.2)" }  // Gray
    ];

    // If no chart yet, or series count changed, (re)build the chart.
    if (!queueChart || queueChart.data.datasets.length !== series.length) {
        if (queueChart) {
            queueChart.destroy();
            queueChart = null;
        }

        queueChart = new Chart(ctx, {
            type: "line",
            data: {
                labels: labels,
                datasets: series.map((s, i) => {
                    const color = colors[i % colors.length];
                    return {
                        label: s.label,
                        data: s.values,
                        borderColor: color.border,
                        backgroundColor: color.background,
                        borderWidth: 2,
                        tension: 0.3,
                        fill: true,
                        pointRadius: 0,
                        pointHoverRadius: 0,
                        hitRadius: 6
                    };
                })
            },
            options: {
                responsive: true,
                interaction: {
                    mode: "index",
                    intersect: false
                },
                animation: {
                    duration: 800,
                    easing: "easeOutQuad"
                },
                scales: {
                    // default two axes – can add more, but they seem to be auto-added.
                    y: {
                        type: "linear",
                        position: "left",
                        beginAtZero: true
                    },
                    y1: {
                        type: "linear",
                        position: "right",
                        beginAtZero: true,
                        grid: { drawOnChartArea: false } // don’t double-draw grid
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

    queueChart.update();
}

document.addEventListener("DOMContentLoaded", async function () {
    await buildOrUpdateChart();
    setInterval(buildOrUpdateChart, 5000);
});
