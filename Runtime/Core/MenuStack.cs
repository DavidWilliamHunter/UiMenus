using System;
using System.Collections.Generic;

namespace Threads.UiMenus
{
    /// <summary>What to do with unapplied changes when leaving a page.</summary>
    public enum LeaveChoice
    {
        /// <summary>Apply them (leaving fails if a check fails).</summary>
        Apply,
        /// <summary>Revert them.</summary>
        Discard,
    }

    /// <summary>
    /// The open pages of a menu, each its own <see cref="MenuSession"/> (Apply, Revert and Cancel
    /// act per object). Leaving a page with unapplied changes needs a decision: ask the user
    /// "Apply, discard or stay?" when <see cref="NeedsDecision"/> is true, then call
    /// <see cref="Back(LeaveChoice)"/> or <see cref="Switch(MenuSession, LeaveChoice)"/>.
    /// </summary>
    public sealed class MenuStack
    {
        readonly List<MenuSession> _pages = new List<MenuSession>();

        public IReadOnlyList<MenuSession> Pages => _pages;
        public MenuSession Current => _pages.Count > 0 ? _pages[_pages.Count - 1] : null;
        public bool IsEmpty => _pages.Count == 0;

        /// <summary>The current page has unapplied changes, so leaving it needs a choice.</summary>
        public bool NeedsDecision => Current != null && Current.IsDirty;

        public event Action<MenuSession> Opened;
        public event Action<MenuSession> Left;

        /// <summary>Opens a page on top (e.g. a <c>[MenuLink]</c> or a sub-menu).</summary>
        public void Push(MenuSession page)
        {
            if (page == null) throw new ArgumentNullException(nameof(page));
            _pages.Add(page);
            Opened?.Invoke(page);
        }

        /// <summary>Goes back when there is nothing to decide. Returns false if the page has
        /// unapplied changes (ask, then use <see cref="Back(LeaveChoice)"/>) or nothing is open.</summary>
        public bool Back()
        {
            if (Current == null || NeedsDecision) return false;
            Leave();
            return true;
        }

        /// <summary>Goes back, applying or discarding unapplied changes first. Returns false (and stays)
        /// if Apply was chosen and a check failed.</summary>
        public bool Back(LeaveChoice choice)
        {
            if (Current == null) return false;
            if (!Settle(choice)) return false;
            Leave();
            return true;
        }

        /// <summary>Replaces the current page (e.g. another tab). Returns false (and stays) if Apply
        /// was chosen and a check failed.</summary>
        public bool Switch(MenuSession next, LeaveChoice choice)
        {
            if (next == null) throw new ArgumentNullException(nameof(next));
            if (Current != null)
            {
                if (!Settle(choice)) return false;
                Leave();
            }
            Push(next);
            return true;
        }

        /// <summary>Cancels every open page (top first) and empties the stack.</summary>
        public void CancelAll()
        {
            while (Current != null)
            {
                var page = Current;
                _pages.RemoveAt(_pages.Count - 1);
                page.Cancel();
                Left?.Invoke(page);
            }
        }

        bool Settle(LeaveChoice choice)
        {
            if (!Current.IsDirty) return true;
            if (choice == LeaveChoice.Apply) return Current.Apply().Applied;
            Current.Revert();
            return true;
        }

        void Leave()
        {
            var page = Current;
            _pages.RemoveAt(_pages.Count - 1);
            page.Close();
            Left?.Invoke(page);
        }
    }
}
