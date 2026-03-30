export type ItemType = 'Series' | 'Playlist' | 'BoxSet'

export interface ExclusionItem {
  type: ItemType
  id: string
  name: string
}

export interface UserSetting {
  UserId: string
  ExclusionItems: ExclusionItem[]
}

export interface PluginConfig {
  UserSettings: UserSetting[]
}

export const emptyUserSetting = (userId: string): UserSetting => ({
  UserId: userId,
  ExclusionItems: [],
})

export const TYPE_LABEL: Record<ItemType, string> = {
  Series:   'Series',
  Playlist: 'Playlist',
  BoxSet:   'Collection',
}
