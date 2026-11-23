import type { Routes } from "@angular/router";
import { LibraryPage } from "./libraryPage";
import { RoomEditorPage } from "./roomEditorPage";
import { RoomListPage } from "./roomListPage";

export const routes: Routes = [
  { path: "", pathMatch: "full", redirectTo: "rooms" },
  { path: "rooms", component: RoomListPage },
  { path: "rooms/:id", component: RoomEditorPage },
  { path: "library", component: LibraryPage },
  { path: "**", redirectTo: "rooms" },
];
