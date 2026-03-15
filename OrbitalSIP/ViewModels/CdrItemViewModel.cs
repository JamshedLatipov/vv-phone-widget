using System;
using Avalonia.Media;
using OrbitalSIP.Models;

namespace OrbitalSIP.ViewModels
{
    public class CdrItemViewModel
    {
        public CdrEntry Entry { get; }
        public string DisplayNumber { get; }
        public string DisplayTime { get; }
        public string IconData { get; }
        public string IconColor { get; }
        public string DisplayStatus { get; }
        public bool IsLogged { get; }
        public string ScriptIconColor { get; }

        public CdrItemViewModel(CdrEntry entry, string currentOperator)
        {
            Entry = entry;

            bool isIncoming = string.Equals(entry.Direction, "inbound", StringComparison.OrdinalIgnoreCase);

                        // Ensure we don't display our own username or extension by explicitly comparing with currentOperator
            if (string.Equals(entry.Src, currentOperator, StringComparison.OrdinalIgnoreCase))
            {
                DisplayNumber = entry.Dst ?? entry.Destination ?? "Unknown";
            }
            else if (string.Equals(entry.Dst, currentOperator, StringComparison.OrdinalIgnoreCase))
            {
                DisplayNumber = entry.Src ?? entry.Caller ?? "Unknown";
            }
            else
            {
                DisplayNumber = isIncoming ? (entry.Caller ?? entry.Src ?? "Unknown") : (entry.Destination ?? entry.Dst ?? "Unknown");
            }

            DisplayTime = entry.CallDate.ToLocalTime().ToString("t"); // e.g., 10:45 AM

            bool isAnswered = string.Equals(entry.Disposition, "ANSWERED", StringComparison.OrdinalIgnoreCase);

            if (isIncoming)
            {
                // Incoming icon
                IconData = "M20,15.5C18.8,15.5 17.5,15.3 16.4,14.9C16.3,14.9 16.2,14.9 16.1,15L13.3,17.8C10.3,16.3 7.7,13.7 6.2,10.7L9,7.9C9.1,7.8 9.1,7.7 9.1,7.6C8.7,6.5 8.5,5.2 8.5,4C8.5,3.5 8,3 7.5,3H4C3.5,3 3,3.5 3,4C3,13.4 10.6,21 20,21C20.5,21 21,20.5 21,20V16.5C21,16 20.5,15.5 20,15.5M5,1.5L9,5.5L5,9.5V6.5H1V3.5H5V1.5Z";
            }
            else
            {
                // Outgoing icon
                IconData = "M20,15.5C18.8,15.5 17.5,15.3 16.4,14.9C16.3,14.9 16.2,14.9 16.1,15L13.3,17.8C10.3,16.3 7.7,13.7 6.2,10.7L9,7.9C9.1,7.8 9.1,7.7 9.1,7.6C8.7,6.5 8.5,5.2 8.5,4C8.5,3.5 8,3 7.5,3H4C3.5,3 3,3.5 3,4C3,13.4 10.6,21 20,21C20.5,21 21,20.5 21,20V16.5C21,16 20.5,15.5 20,15.5M21,6.5H17V9.5L13,5.5L17,1.5V4.5H21V6.5Z";
            }

            // Red if missed/failed, Green if answered
            IconColor = isAnswered ? "#22C55E" : "#EF4444";
            DisplayStatus = isAnswered ? "Answered" : (entry.Disposition ?? "Unknown");

            // Check if call was logged
            if (!string.IsNullOrEmpty(entry.UniqueId) && !string.IsNullOrEmpty(currentOperator))
            {
                IsLogged = App.LoggedCallService.IsCallLogged(entry.UniqueId, currentOperator);
            }
            else
            {
                IsLogged = false;
            }

            ScriptIconColor = IsLogged ? "#10B981" : "#60A5FA";
        }
    }
}
