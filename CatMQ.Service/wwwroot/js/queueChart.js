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
        { border: "rgb(75, 192, 192)", background: "rgba(75, 192, 192, 0.2)" },
        { border: "rgb(255, 99, 132)", background: "rgba(255, 99, 132, 0.2)" },
        { border: "rgb(54, 162, 235)", background: "rgba(54, 162, 235, 0.2)" }
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
                        fill: true
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
                    y: { beginAtZero: true }
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
    });

    queueChart.update();
}

document.addEventListener("DOMContentLoaded", async function () {
    await buildOrUpdateChart();
    setInterval(buildOrUpdateChart, 5000);
});
