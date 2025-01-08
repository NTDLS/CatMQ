using System.Text;

namespace CatMQ.Service
{
    public static class ConfirmActionHelper
    {
        /// <summary>
        /// Generates a link that navigates via GET to a "confirm action" page where the yes link is RED, but the NO button is still GREEN.
        /// </summary>
        public static string GenerateDangerLink(string message, string linkLabel, string postBackTo, string style = "")
        {

            var param = new StringBuilder();
            param.Append($"PostBackTo={Uri.EscapeDataString($"{postBackTo}")}");
            param.Append($"&Message={Uri.EscapeDataString(message)}");
            param.Append($"&Style=Danger");

            return $"<a class=\"btn btn-danger {style}\" href=\"/ConfirmAction?{param}\">{linkLabel}</a>";
        }

        /// <summary>
        /// Generates a link that navigates via GET to a "confirm action" page where the yes link is GREEN.
        /// </summary>
        public static string GenerateSafeLink(string message, string linkLabel, string postBackTo, string style = "")
        {
            var param = new StringBuilder();
            param.Append($"PostBackTo={Uri.EscapeDataString($"{postBackTo}")}");
            param.Append($"&Message={Uri.EscapeDataString(message)}");
            param.Append($"&Style=Safe");

            return $"<a class=\"btn btn-success {style}\" href=\"/ConfirmAction?{param}\">{linkLabel}</a>";
        }

        /// <summary>
        /// Generates a link that navigates via GET to a "confirm action" page where the yes link is YELLOW, but the NO button is still GREEN.
        /// </summary>
        public static string GenerateWarnLink(string message, string linkLabel, string postBackTo, string style = "")
        {
            var param = new StringBuilder();
            param.Append($"PostBackTo={Uri.EscapeDataString($"{postBackTo}")}");
            param.Append($"&Message={Uri.EscapeDataString(message)}");
            param.Append($"&Style=Warn");

            return $"<a class=\"btn btn-warning {style}\" href=\"/ConfirmAction?{param}\">{linkLabel}</a>";
        }
    }
}
