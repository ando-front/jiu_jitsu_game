# Archive

廃止されたが履歴として残しておくコードツリー。

| 項目 | 廃止日 | 理由 |
|---|---|---|
| [ue5_abandoned/](./ue5_abandoned/) | 2026-04-23 | Stage 2 のエンジン選定を **Unity に変更**。軽量化と Mac 開発対応が決め手。現行の Stage 2 コードは [`src/prototype/unity/`](../src/prototype/unity/)。UE5 版は HandFSM + scaffold まで進んでいたが、port plan の直訳パターン自体は Unity でも同じ設計意図で再利用できる(enum / 純関数 tick / sentinel pattern)。
