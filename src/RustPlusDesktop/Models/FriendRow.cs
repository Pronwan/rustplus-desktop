using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace RustPlusDesk.Models;

public enum FriendRowKind
{
    Friend,

    /// <summary>They asked us: the row carries Accept and Decline.</summary>
    Incoming,

    /// <summary>We asked them: the row carries Cancel.</summary>
    Outgoing,
}

public enum FriendRowMode
{
    Normal,
    ConfirmRemove,
    ConfirmBlock,
}

/// <summary>
/// One row of the friends pane: a <see cref="Friend"/> plus what the row itself is doing.
///
/// The extra state is why it exists. "Are you sure?" lives in the row it is asking about, and a
/// refresh arriving from the server must not throw that question away, so rows are kept between
/// refreshes and only their friend is swapped.
/// </summary>
public sealed class FriendRow : INotifyPropertyChanged
{
    private FriendRowMode _mode;
    private bool _alsoRemoveOnBlock = true;
    private bool _isBusy;

    public FriendRow(Friend friend, FriendRowKind kind)
    {
        Friend = friend;
        Kind = kind;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public Friend Friend { get; private set; }

    public FriendRowKind Kind { get; }

    public string Key => $"{Kind}:{Friend.Id}";

    public string DisplayName => Friend.DisplayName;

    public string? AvatarUrl => Friend.AvatarUrl;

    public string UserId => Friend.UserId;

    public bool IsOnline => Friend.IsOnline;

    public bool IsPlaying => Kind == FriendRowKind.Friend && Friend.Server is { Length: > 0 };

    public bool IsIncoming => Kind == FriendRowKind.Incoming;

    public bool IsOutgoing => Kind == FriendRowKind.Outgoing;

    public bool IsFriend => Kind == FriendRowKind.Friend;

    public Visibility IncomingVisibility => Show(IsIncoming);

    public Visibility OutgoingVisibility => Show(IsOutgoing);

    public Visibility FriendVisibility => Show(IsFriend);

    /// <summary>
    /// The one line under the name: what the request is, or where the friend is.
    ///
    /// Not in a server is not the same as offline: somebody can have the app open between wipes,
    /// and the two must not read the same.
    /// </summary>
    public string StatusText => Kind switch
    {
        FriendRowKind.Incoming => Text("FriendsRequestIncoming"),
        FriendRowKind.Outgoing => Text("FriendsRequestOutgoing"),
        _ when Friend.Server is { Length: > 0 } server => Friend.TeamSize > 1
            ? string.Format(Text("FriendsPlayingOnTeam"), server, Friend.TeamSize)
            : string.Format(Text("FriendsPlayingOn"), server),
        _ when Friend.IsOnline => Text("FriendsOnlineNow"),
        _ => Text("FriendsOffline"),
    };

    public FriendRowMode Mode
    {
        get => _mode;
        set
        {
            if (_mode == value) return;
            _mode = value;
            Raise();
            Raise(nameof(NormalVisibility));
            Raise(nameof(ConfirmRemoveVisibility));
            Raise(nameof(ConfirmBlockVisibility));
        }
    }

    public Visibility NormalVisibility => Show(_mode == FriendRowMode.Normal);

    public Visibility ConfirmRemoveVisibility => Show(_mode == FriendRowMode.ConfirmRemove);

    public Visibility ConfirmBlockVisibility => Show(_mode == FriendRowMode.ConfirmBlock);

    /// <summary>
    /// Blocking somebody you had as a friend ends the friendship by default: leaving the row behind
    /// would be a friends list containing somebody you cannot reach. The checkbox lets people keep it.
    /// </summary>
    public bool AlsoRemoveOnBlock
    {
        get => _alsoRemoveOnBlock;
        set
        {
            if (_alsoRemoveOnBlock == value) return;
            _alsoRemoveOnBlock = value;
            Raise();
        }
    }

    /// <summary>True while a request for this row is in flight, so a double click cannot send two.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (_isBusy == value) return;
            _isBusy = value;
            Raise();
            Raise(nameof(IsIdle));
        }
    }

    public bool IsIdle => !_isBusy;

    public string RemovePrompt => string.Format(Text("FriendsRemoveConfirm"), DisplayName);

    public string BlockPrompt => string.Format(Text("FriendsBlockConfirm"), DisplayName);

    /// <summary>A newer read of the same friendship: swap the data, keep the row's own state.</summary>
    public void Update(Friend friend)
    {
        Friend = friend;
        Raise(null);
    }

    private static Visibility Show(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    private static string Text(string key) => Properties.Resources.GetString(key);

    private void Raise([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
