<script setup lang="ts">
const paths = {
  home: 'M3 11 12 3l9 8M5 10v10h5v-6h4v6h5V10',
  records: 'M12 8v4l3 2M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0Z',
  book: 'M12 5v16M3 4c4-1 7 0 9 2 2-2 5-3 9-2v15c-4-1-7 0-9 2-2-2-5-3-9-2Z',
  review: 'M3 17l6-6 4 4 8-10M15 5h6v6',
  settings: 'M9 3h6l1 4 4 1v7l-4 1-1 5H9l-1-5-5-1V8l5-1ZM15 12a3 3 0 1 1-6 0 3 3 0 0 1 6 0Z',
  bell: 'M18 8a6 6 0 0 0-12 0c0 7-3 7-3 9h18c0-2-3-2-3-9M10 21h4',
  volume: 'M4 9h4l5-4v14l-5-4H4ZM17 8a6 6 0 0 1 0 8M20 5a10 10 0 0 1 0 14',
  arrow: 'M5 12h14M13 6l6 6-6 6',
  search: 'M21 21l-5-5M18 10a8 8 0 1 1-16 0 8 8 0 0 1 16 0Z',
  close: 'M6 6l12 12M18 6 6 18',
  shield: 'M12 3l8 3v6c0 5-8 9-8 9s-8-4-8-9V6ZM8 12l3 3 5-6'
}
defineProps<{ name: keyof typeof paths }>()
</script>
<template><svg class="app-icon" width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false"><path :d="paths[name]" /></svg></template>
