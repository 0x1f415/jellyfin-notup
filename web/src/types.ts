export type ItemType = 'Series' | 'Playlist' | 'BoxSet'

export interface ExclusionItem {
  type: ItemType
  id: string
  name: string
}

export interface UserFilterConfig {
  ExclusionItems: ExclusionItem[]
}

export interface PluginConfig {
  UserSettings: Record<string, UserFilterConfig>
}

export const emptyUserConfig = (): UserFilterConfig => ({
  ExclusionItems: [],
})

export const TYPE_LABEL: Record<ItemType, string> = {
  Series:   'Series',
  Playlist: 'Playlist',
  BoxSet:   'Collection',
}
