import type { Friend, FriendState } from "./types";

/** What a player card's buttons do (the hub's AddFriend, AcceptFriend, DeclineFriend, CancelFriendRequest, RemoveFriend). */
export type FriendAction = "add" | "accept" | "decline" | "cancel" | "remove";

export interface FriendButton {
  action: FriendAction;
  label: string;
  primary: boolean;
}

/** Where the player is now: the room showing. */
export interface Here {
  worldId: string;
  roomId: string;
}

/** A line saying where the two stand, for the card; none when there's nothing between them. */
export function friendStatus(state: FriendState): string | undefined {
  switch (state) {
    case "RequestSent":
      return "Friend request sent.";
    case "RequestReceived":
      return "Wants to be friends!";
    case "Friends":
      return "You're friends.";
    case "None":
      return undefined;
  }
}

/** The card's buttons for where the two stand. */
export function friendButtons(state: FriendState): FriendButton[] {
  switch (state) {
    case "None":
      return [{ action: "add", label: "Add friend", primary: true }];
    case "RequestSent":
      return [{ action: "cancel", label: "Cancel request", primary: false }];
    case "RequestReceived":
      return [
        { action: "accept", label: "Accept", primary: true },
        { action: "decline", label: "Decline", primary: false },
      ];
    case "Friends":
      return [{ action: "remove", label: "Remove friend", primary: false }];
  }
}

/** Whether the friend is in the room the player is in. */
export function isHere(friend: Friend, here: Here | undefined): boolean {
  return !!friend.place && !!here && friend.place.worldId === here.worldId && friend.place.roomId === here.roomId;
}

/** Where a friend is, as the friends list says it. */
export function whereIs(friend: Friend, here: Here | undefined): string {
  if (!friend.place) {
    return "Offline";
  }
  if (isHere(friend, here)) {
    return "Here with you";
  }
  return `${friend.place.roomName}, ${friend.place.worldName}`;
}

/** Whether "Go to" can take the player to the friend: online, and somewhere else. */
export function canGoTo(friend: Friend, here: Here | undefined): boolean {
  return friend.friendState === "Friends" && !!friend.place && !isHere(friend, here);
}

/** The list in its three parts, in the server's order: friends, requests to the player, and the player's requests. */
export function sections(list: Friend[]): { friends: Friend[]; received: Friend[]; sent: Friend[] } {
  return {
    friends: list.filter((f) => f.friendState === "Friends"),
    received: list.filter((f) => f.friendState === "RequestReceived"),
    sent: list.filter((f) => f.friendState === "RequestSent"),
  };
}
