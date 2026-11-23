import type { Routes } from "@angular/router";
import { LibraryPage } from "./libraryPage";
import { RoomEditorPage } from "./roomEditorPage";
import { RoomListPage } from "./roomListPage";
import { RoomMapPage } from "./roomMapPage";

export const routes: Routes = [
  { path: "", pathMatch: "full", redirectTo: "rooms" },
  { path: "rooms", component: RoomListPage },
  { path: "map", component: RoomMapPage },
  { path: "rooms/:id", component: RoomEditorPage },
  { path: "library", component: LibraryPage },
  { path: "**", redirectTo: "rooms" },
];
