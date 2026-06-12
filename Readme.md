# MySuperToDo

MySuperToDo is a Blazor WebAssembly application powered by **GunDB** ([https://github.com/gundb/gun](https://github.com/gundb/gun)), a local‑first, peer‑to‑peer, encrypted graph database.
The app demonstrates a clean, minimal domain model for managing tasks while using GunDB for storage, sync, and offline capability.

## How this ToDo app differs from others:
- Most ToDo apps use a relational or document‑oriented database, while MySuperToDo uses a graph database (GunDB) that treats ToDo items as nodes with properties and relationships.
- Using a relational or document database often requires recreating copies ToDo items to copy them to other lists, which can lead to data duplication and complexity. Say you want to have a task be in several different lists. You might want to have "buy milk" be on a general "buy groceries" list, but also on a "buy today" list. Typically, you would copy "buy milk" from "buy groceries" to "buy today". But now you have 2 seperate ToDo items. Completing one, on "buy today" will not show it as done in the "buy groceries" list.
- MySuperToDo works differently. Each todo item is a "stand-alone" item. Adding it to a list, or copying from one list to another, doesn't recreate the item. It simply adds "relationships" to the item. When you mark the item complete (or change its state some other way), it immediately shows up in the same state in all other lists - because all those lists are just "pointing" to the one single item. This is why a graph database was chosen. Graphs manage relationships between nodes. The one ToDo item is simply a node. It can be "related" to anything else in the graph while remaining as a single node.

- GunDB was chosen because:
	- it's a graph database
	- it stores data locally in the browser
	- it can replicated across the entire network of relays
	- your data belongs to you and ONLY you. Even if it is distributed across hundreds of relays, it is your data and ONLY your data. It is cryptographically tied to your identity. **NOTE** this definitely requires thinking about data in a very different manner. I strongly suggest reading the GunDB documentation
- Reticle was chosen because it allows for "separating" the data in a gundb database between apps. One problem with a database like GunDB is it's just one giant "pool" of data. It's all yours, but conceptually, your MySuperToDo app data is not discrete from your MyOtherApp data in the giant pool. Reticle provides the sort of scoping necessary to set the data from one app you use from another app you use (assuming both apps are using GunDB).

The development process was very different for me. I really wanted to learn how to "vibe" code using Copilot in my IDE. So ALL of the code was written by Copilot with me only providing prompts for features, prompts to correct incorrect behaviours of the app, etc. It was VERY instructive. I also ran out of tokens pretty quickly with the free version & finally stepped up and got a paid license.

Run the app here:
https://<username>.github.io/MySuperToDo/

## 📌 Overview

MySuperToDo stores tasks as GunDB graph nodes.
Each task includes:

- **Status** — required string
- **Title** — required string
- **StatusDate** — optional ISO date (`YYYY-MM-DD`)
- **Notes** — optional string

GunDB handles:

- Local persistence
- Optional sync to a relay (Node.js)
- End‑to‑end encryption via SEA
- Offline‑first behavior

The app is built as a **client‑side Blazor WebAssembly** application and deployed as a static site via GitHub Pages.

## 🧰 Tech Stack

- .NET (Blazor WebAssembly)
- Radzen free Blazor components
- GunDB (client-side JS via JSInterop)
- Reticle (library to "scope" the data to MySuperToDo)
- Optional GunDB relay (Node.js)
- GitHub Pages static hosting

## 🗂️ Data Model

GunDB stores each `ToDoItem` as a node in the graph:

```
gun.get('todos').get(<id>).put({
  Status: "...",
  Title: "...",
  StatusDate: "...",
  Notes: "..."
})
```

GunDB automatically:

- Generates IDs
- Handles conflict resolution (CRDT)
- Syncs to the relay if configured

## 🔌 Relay (Optional)

If using a relay, it typically runs on:

```
node server.js
```

Your relay might be hosted on a Raspberry Pi or VPS.
The relay stores encrypted data but **cannot decrypt it** — all encryption happens client‑side.
Instructions for setting up a relay are included in the `gundb` folder of the repository.

The app as loaded from github does not point to a relay, so your data will be local ONLY to the browser you use to load MySuperToDo. You can use the user settings page (dropdown from the usr icon) to enter a relay to use. I have a relay set up in my home office running on my Raspberry Pi. Setting it up was, well, as easy as pie...

## 🧪 Running the App Locally

```bash
dotnet run
```

The Blazor client loads GunDB via JSInterop and connects to:

- Local storage
- Optional relay URL (configured in the app)

## 📦 Publishing for GitHub Pages

Build and publish:

```bash
dotnet publish -c Release
```

The deployable static site files are located in:

```
/bin/Release/netX.Y/publish/wwwroot
```

A GitHub Action rewrites the `<base href>` in `index.html` so the app loads correctly from the GitHub Pages subpath.

## 📁 Repository Structure

```
/Client        # Blazor WebAssembly UI
/wwwroot       # Static assets, GunDB scripts, index.html
/gundb         # Optional relay scripts (Node.js)
/Shared        # Shared models
```

## 📄 License

MIT
