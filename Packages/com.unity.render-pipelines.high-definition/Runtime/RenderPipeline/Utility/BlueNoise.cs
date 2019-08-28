using UnityEngine.Assertions;

namespace UnityEngine.Rendering.HighDefinition
{
    // Blue noise texture bank
    public sealed class BlueNoise
    {
        public Texture2D[] textures16L { get { return m_Textures16L; } }
        public Texture2D[] textures16RGB { get { return m_Textures16RGB; } }

        public Texture2DArray textureArray16L { get { return m_TextureArray16L; } }
        public Texture2DArray textureArray16RGB { get { return m_TextureArray16RGB; } }

        readonly Texture2D[] m_Textures16L;
        readonly Texture2D[] m_Textures16RGB;

        Texture2DArray m_TextureArray16L;
        Texture2DArray m_TextureArray16RGB;

        RenderPipelineResources m_RenderPipelineResources;

        static readonly System.Random m_Random = new System.Random();

        public BlueNoise(RenderPipelineResources resources)
        {
            m_RenderPipelineResources = resources;
            InitTextures(16, TextureFormat.Alpha8, resources.textures.blueNoise16LTex, out m_Textures16L, out m_TextureArray16L);
            InitTextures(16, TextureFormat.RGB24, resources.textures.blueNoise16RGBTex, out m_Textures16RGB, out m_TextureArray16RGB);
        }

        public void Cleanup()
        {
            CoreUtils.Destroy(m_TextureArray16L);
            CoreUtils.Destroy(m_TextureArray16RGB);

            m_TextureArray16L = null;
            m_TextureArray16RGB = null;
        }

        public Texture2D GetRandom16L()
        {
            return textures16L[(int)(m_Random.NextDouble() * (textures16L.Length - 1))];
        }

        public Texture2D GetRandom16RGB()
        {
            return textures16RGB[(int)(m_Random.NextDouble() * (textures16RGB.Length - 1))];
        }

        static void InitTextures(int size, TextureFormat format, Texture2D[] sourceTextures, out Texture2D[] destination, out Texture2DArray destinationArray)
        {
            Assert.IsNotNull(sourceTextures);

            int len = sourceTextures.Length;

            Assert.IsTrue(len > 0);

            destination = new Texture2D[len];
            destinationArray = new Texture2DArray(size, size, len, format, false, true);
            destinationArray.hideFlags = HideFlags.HideAndDontSave;

            for (int i = 0; i < len; i++)
            {
                var noiseTex = sourceTextures[i];

                // Fail safe; should never happen unless the resources asset is broken
                if (noiseTex == null)
                {
                    destination[i] = Texture2D.whiteTexture;
                    continue;
                }

                destination[i] = noiseTex;
                Graphics.CopyTexture(noiseTex, 0, 0, destinationArray, i, 0);
            }
        }

        public void BindDitheredRNGData1SPP(CommandBuffer cmd)
        {
            cmd.SetGlobalTexture(HDShaderIDs._OwenScrambledTexture, m_RenderPipelineResources.textures.owenScrambled256Tex);
            cmd.SetGlobalTexture(HDShaderIDs._ScramblingTileXSPP, m_RenderPipelineResources.textures.scramblingTile1SPP);
            cmd.SetGlobalTexture(HDShaderIDs._RankingTileXSPP, m_RenderPipelineResources.textures.rankingTile1SPP);
            cmd.SetGlobalTexture(HDShaderIDs._ScramblingTexture, m_RenderPipelineResources.textures.scramblingTex);
        }

        public void BindDitheredRNGData8SPP(CommandBuffer cmd)
        {
            cmd.SetGlobalTexture(HDShaderIDs._OwenScrambledTexture, m_RenderPipelineResources.textures.owenScrambled256Tex);
            cmd.SetGlobalTexture(HDShaderIDs._ScramblingTileXSPP, m_RenderPipelineResources.textures.scramblingTile8SPP);
            cmd.SetGlobalTexture(HDShaderIDs._RankingTileXSPP, m_RenderPipelineResources.textures.rankingTile8SPP);
            cmd.SetGlobalTexture(HDShaderIDs._ScramblingTexture, m_RenderPipelineResources.textures.scramblingTex);
        }

        public void BindDitheredRNGData256SPP(CommandBuffer cmd)
        {
            cmd.SetGlobalTexture(HDShaderIDs._OwenScrambledTexture, m_RenderPipelineResources.textures.owenScrambled256Tex);
            cmd.SetGlobalTexture(HDShaderIDs._ScramblingTileXSPP, m_RenderPipelineResources.textures.scramblingTile256SPP);
            cmd.SetGlobalTexture(HDShaderIDs._RankingTileXSPP, m_RenderPipelineResources.textures.rankingTile256SPP);
            cmd.SetGlobalTexture(HDShaderIDs._ScramblingTexture, m_RenderPipelineResources.textures.scramblingTex);
        }
    }
}
