using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

namespace Buffer
{
    public sealed class ClipboardHistory : INotifyPropertyChanged
    {
        // Windows хранит 25 элементов. Незакреплённых по умолчанию держим вдвое больше, закреплённые не ограничены.
        public const int DefaultLimit = 50;

        bool _unlimited;

        public ObservableCollection<ClipItem> Items { get; } = new ObservableCollection<ClipItem>();

        public bool Unlimited
        {
            get => _unlimited;
            set
            {
                if (_unlimited == value)
                    return;
                _unlimited = value;
                if (Trim())
                    OnChanged();
            }
        }

        public bool IsEmpty => Items.Count == 0;
        public bool HasUnpinned => Items.Any(i => !i.IsPinned);

        public event PropertyChangedEventHandler PropertyChanged;

        // Изменилось то, что нужно сохранить на диск.
        public event Action Changed;

        public void Add(ClipItem item, bool moveExistingToTop = true)
        {
            var existing = FindSame(item);
            if (existing != null)
            {
                if (moveExistingToTop)
                    MoveToTop(existing);
                return;
            }
            Items.Insert(0, item);
            Trim();
            OnChanged();
        }

        public void AddRestored(IEnumerable<ClipItem> items)
        {
            foreach (var item in items)
            {
                if (FindSame(item) == null)
                    Items.Add(item);
            }
            Trim();
            NotifyState();
        }

        public void MoveToTop(ClipItem item)
        {
            int index = Items.IndexOf(item);
            if (index < 0)
                return;
            if (index > 0)
                Items.Move(index, 0);
            OnChanged();
        }

        public void Remove(ClipItem item)
        {
            if (Items.Remove(item))
                OnChanged();
        }

        public void TogglePin(ClipItem item)
        {
            item.IsPinned = !item.IsPinned;
            Trim();
            OnChanged();
        }

        public void ClearUnpinned()
        {
            for (int i = Items.Count - 1; i >= 0; i--)
            {
                if (!Items[i].IsPinned)
                    Items.RemoveAt(i);
            }
            OnChanged();
        }

        public void RemovePrivateUnpinned()
        {
            for (int i = Items.Count - 1; i >= 0; i--)
            {
                if (Items[i].IsPrivate && !Items[i].IsPinned)
                    Items.RemoveAt(i);
            }
            OnChanged();
        }

        public void RefreshTexts()
        {
            foreach (var item in Items)
                item.RefreshTexts();
        }

        ClipItem FindSame(ClipItem item) =>
            Items.FirstOrDefault(i => i.Kind == item.Kind && i.Hash == item.Hash &&
                (i.Kind != ClipKind.Text || string.Equals(i.Text, item.Text, StringComparison.Ordinal)));

        bool Trim()
        {
            if (_unlimited)
                return false;
            bool removed = false;
            int unpinned = 0;
            for (int i = 0; i < Items.Count; i++)
            {
                if (Items[i].IsPinned)
                    continue;
                if (++unpinned > DefaultLimit)
                {
                    Items.RemoveAt(i);
                    i--;
                    removed = true;
                }
            }
            return removed;
        }

        void OnChanged()
        {
            NotifyState();
            Changed?.Invoke();
        }

        void NotifyState()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEmpty)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasUnpinned)));
        }
    }
}
