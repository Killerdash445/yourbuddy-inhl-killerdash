namespace YourBuddy
{
    internal sealed class ResourceProbeBudget(int limit)
    {
        private int frame = -1, used;
        internal bool Take(int currentFrame)
        {
            if (frame != currentFrame) { frame = currentFrame; used = 0; }
            return used++ < limit;
        }
    }
}
