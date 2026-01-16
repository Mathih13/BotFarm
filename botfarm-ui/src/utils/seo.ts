export function seo({
  title,
  description,
  keywords,
  image,
}: {
  title: string
  description?: string
  keywords?: string
  image?: string
}) {
  const tags = [
    { title },
    { name: 'description', content: description },
    { name: 'keywords', content: keywords },
    { name: 'twitter:title', content: title },
    { name: 'twitter:description', content: description },
    { name: 'twitter:card', content: 'summary' },
    { name: 'twitter:image', content: image },
    { name: 'og:type', content: 'website' },
    { name: 'og:title', content: title },
    { name: 'og:description', content: description },
    { name: 'og:image', content: image },
  ]

  return tags.filter(
    (tag) => tag.content !== undefined && tag.content !== ''
  )
}
