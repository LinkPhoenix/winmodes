using System.Collections.ObjectModel;
using System.ComponentModel;

namespace WinModes.App.Controls;

/// <summary>
/// Stable item for one row of a long list: swapping <see cref="Data"/> refreshes the row without recreating it, so a
/// refresh neither rebuilds the visuals nor sends the list back to the top.
/// </summary>
internal sealed class RowHolder<T>(T data, object key) : INotifyPropertyChanged
    where T : class
{
    private T _data = data;

    public event PropertyChangedEventHandler? PropertyChanged;

    public object Key { get; } = key;

    public T Data
    {
        get => _data;
        set
        {
            if (!EqualityComparer<T>.Default.Equals(_data, value))
            {
                _data = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Data)));
            }
        }
    }
}

internal static class RowHolderList
{
    /// <summary>Makes <paramref name="target"/> show <paramref name="rows"/> in order, reusing the holder of every row that is still there.</summary>
    public static void Reconcile<T>(this ObservableCollection<RowHolder<T>> target, IReadOnlyList<T> rows, Func<T, object> keyOf)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(keyOf);

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var key = keyOf(row);
            if (i < target.Count && target[i].Key.Equals(key))
            {
                target[i].Data = row;
                continue;
            }

            var existing = -1;
            for (var j = i + 1; j < target.Count; j++)
            {
                if (target[j].Key.Equals(key))
                {
                    existing = j;
                    break;
                }
            }

            if (existing >= 0)
            {
                target.Move(existing, i);
                target[i].Data = row;
            }
            else
            {
                target.Insert(i, new RowHolder<T>(row, key));
            }
        }

        while (target.Count > rows.Count)
        {
            target.RemoveAt(target.Count - 1);
        }
    }

    /// <summary>
    /// Makes <paramref name="target"/> hold <paramref name="items"/> in order, comparing by reference: what is still there is kept (and so is
    /// its visual and the scroll position), only what is new or gone is added or removed.
    /// </summary>
    public static void ReconcileItems<T>(this ObservableCollection<T> target, IReadOnlyList<T> items)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(items);

        for (var i = 0; i < items.Count; i++)
        {
            if (i < target.Count && ReferenceEquals(target[i], items[i]))
            {
                continue;
            }

            var existing = -1;
            for (var j = i + 1; j < target.Count; j++)
            {
                if (ReferenceEquals(target[j], items[i]))
                {
                    existing = j;
                    break;
                }
            }

            if (existing >= 0)
            {
                target.Move(existing, i);
            }
            else
            {
                target.Insert(i, items[i]);
            }
        }

        while (target.Count > items.Count)
        {
            target.RemoveAt(target.Count - 1);
        }
    }
}
