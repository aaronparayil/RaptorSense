namespace RaptorSense.Input;

/// <summary>Reports which lighting cell was pressed. Only cell numbers leave the source: no characters, nothing stored.</summary>
public interface IKeyPressSource : IDisposable
{
    /// <summary>Raised once per physical press (auto-repeat is ignored), on a background thread. Keep handlers fast.</summary>
    event Action<int>? CellPressed;
}
