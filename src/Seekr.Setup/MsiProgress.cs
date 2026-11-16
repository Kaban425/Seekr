namespace Seekr.Setup;

public sealed class MsiProgress
{
    private long _total;
    private long _position;
    private bool _backward;
    private long _actionTicks;
    private bool _actionData;
    public double Percent => _total > 0 ? Math.Clamp(100d * _position / _total, 0, 100) : 0;
    public bool HasTotal => _total > 0;
    public void Update(int kind, int ticks, int direction)
    {
        ticks = Math.Max(0, ticks);
        switch (kind)
        {
            case 0: _total = ticks; _backward = direction != 0; _position = _backward ? _total : 0; _actionData = false; break;
            case 1: _actionTicks = ticks; _actionData = direction != 0; break;
            case 2: _position += _backward ? -ticks : ticks; break;
            case 3: _total += ticks; break;
        }
    }
    public void ActionData() { if (_actionData) _position += _backward ? -_actionTicks : _actionTicks; }
}
