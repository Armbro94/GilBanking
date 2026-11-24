// Gemeinsame Namespaces der Unterordner, damit sich die Module ohne lange using-Listen sehen.
// Hinweis: Configuration und WatchItem bleiben bewusst im Namespace "GilBanking" (Data/Configuration.cs):
// Dalamud speichert den Typnamen in der Konfigurationsdatei, ein anderer Namespace würde die Einstellungen zurücksetzen.
global using GilBanking.Data;
global using GilBanking.Market;
global using GilBanking.Tracking;
global using GilBanking.Windows;
