using System.Collections.ObjectModel;

namespace DotNetConf.App.ViewModels;

internal static class CollectionUpdates
{
    public static void Apply<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        var next = values.ToArray();
        var retained = next.ToHashSet();
        for (var index = target.Count - 1; index >= 0; index--)
            if (!retained.Contains(target[index]))
                target.RemoveAt(index);
        for (var index = 0; index < next.Length; index++)
        {
            var previous = target.IndexOf(next[index]);
            if (previous < 0)
                target.Insert(index, next[index]);
            else if (previous != index)
                target.Move(previous, index);
        }
    }
}
