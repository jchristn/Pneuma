namespace Pneuma.Server.Settings
{
    /// <summary>
    /// DocumentAtom integration settings (type detection and cell extraction).
    /// </summary>
    public class DocumentAtomSettings
    {
        /// <summary>Base URL of the DocumentAtom server.</summary>
        public string Endpoint { get; set; } = "http://127.0.0.1:8000/";
    }
}
