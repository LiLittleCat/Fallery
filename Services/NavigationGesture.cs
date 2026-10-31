namespace Fallery.Services;

public enum NavigationAction { None, Parent, Back, Previous, Next }

public static class NavigationGesture
{
    public static NavigationAction Classify(double dx, double dy)
    {
        if (!double.IsFinite(dx) || !double.IsFinite(dy)) return NavigationAction.None;
        double x = Math.Abs(dx), y = Math.Abs(dy);
        if (Math.Max(x, y) < 48) return NavigationAction.None;
        // 小幅抖动和不明确的斜向拖动不能触发目录跳转。
        if (x >= y * 1.4) return dx < 0 ? NavigationAction.Previous : NavigationAction.Next;
        if (y >= x * 1.4) return dy < 0 ? NavigationAction.Parent : NavigationAction.Back;
        return NavigationAction.None;
    }
}
