import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'
import assert from 'node:assert/strict'
import { createServer } from 'vite'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const output = process.env.L12_QA_OUT || path.resolve(root, '../artifacts/admin-responsive-full')
const require = createRequire(import.meta.url)
const { chromium } = require(process.env.L12_PLAYWRIGHT || 'C:/Users/neptu/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')
fs.mkdirSync(output, { recursive: true })

const baseScript = fs.readFileSync(path.join(root, 'scripts/verify-profile-admin-responsive.mjs'), 'utf8')
const baseEntry = baseScript.match(/const entry = `([\s\S]*?)`\r?\n\r?\nlet browser/)?.[1]
if (!baseEntry) throw new Error('Unable to extract existing admin fixture')

const imports = `
const AdminShell=(await import('/src/l12/site/AdminPage.vue')).default
const components={
 overview:(await import('/src/l12/site/AdminWorkbenchPage.vue')).default,
 accounts:(await import('/src/l12/site/AdminAccountsPage.vue')).default,
 accountDetail:(await import('/src/l12/site/AdminAccountDetailPage.vue')).default,
 renames:(await import('/src/l12/site/AdminUsernameChangeRequestsPanel.vue')).default,
 bugs:(await import('/src/l12/site/AdminBugsPage.vue')).default,
 content:(await import('/src/l12/site/AdminSiteContentPanel.vue')).default,
 rules:(await import('/src/l12/site/AdminRuleRulingsPanel.vue')).default,
 effects:(await import('/src/l12/site/AdminEffectsPage.vue')).default,
 alternateArts:(await import('/src/l12/site/AdminAlternateArtsPanel.vue')).default,
 matches:(await import('/src/l12/site/AdminMatchesPage.vue')).default,
 governance:(await import('/src/l12/site/AdminMatchGovernancePanel.vue')).default,
 integrity:(await import('/src/l12/site/AdminRankedIntegrityPanel.vue')).default,
 tournaments:(await import('/src/l12/site/AdminTournamentWorkbench.vue')).default,
 operations:(await import('/src/l12/site/AdminOperationsPanel.vue')).default,
 globalData:(await import('/src/l12/site/AdminGlobalDataPanel.vue')).default,
 cardAnalytics:(await import('/src/l12/site/AdminCardAnalyticsPage.vue')).default,
 releases:(await import('/src/l12/site/AdminReleasesPage.vue')).default,
 security:(await import('/src/l12/site/AdminSecurityPage.vue')).default,
 storage:(await import('/src/l12/site/AdminServerStoragePanel.vue')).default,
 commands:(await import('/src/l12/site/AdminCommandsPage.vue')).default,
 audit:(await import('/src/l12/site/AdminAuditPage.vue')).default,
}
const routeDefs=[
 ['',components.overview,'overview'],
 ['users/accounts',components.accounts,'accounts'],
 ['users/accounts/:accountId',components.accountDetail,'accounts'],
 ['users/renames',components.renames,'username-requests'],
 ['users/bugs/:bugId?',components.bugs,'bugs'],
 ['content/site',components.content,'content'],
 ['content/rules',components.rules,'rules'],
 ['content/effects/:cardId?',components.effects,'effects'],
 ['content/alternate-arts',components.alternateArts,'alternate-arts'],
 ['matches/archive/:matchId?',components.matches,'matches'],
 ['matches/governance',components.governance,'match-governance'],
 ['matches/integrity',components.integrity,'integrity'],
 ['matches/tournaments',components.tournaments,'tournaments',{adminMode:true,embedded:true}],
 ['operations/config',components.operations,'operations'],
 ['operations/global',components.globalData,'global-data'],
 ['operations/cards',components.cardAnalytics,'card-analytics'],
 ['system/releases',components.releases,'releases'],
 ['system/security',components.security,'security'],
 ['system/storage',components.storage,'storage'],
 ['system/commands',components.commands,'commands'],
 ['system/audit',components.audit,'audit'],
]
const router=createRouter({history:createMemoryHistory(),routes:[
 {path:'/me',component:{template:'<main></main>'}},
 {path:'/admin',component:AdminShell,children:routeDefs.map(([path,component,adminSection,props])=>({path,component,props,meta:{adminSection}}))},
]})
const app=createApp({render:()=>h(RouterView)});app.use(router);await router.push('/admin');await router.isReady();app.mount('#app');window.__qaRouter=router
`

const extraMocks = `
platform.platformState.account.permissions=[...new Set([...(platform.platformState.account.permissions||[]),'admin.operations.read','admin.operations.write','admin.content.read','admin.content.draft','admin.content.publish','admin.content.rollback','admin.matches.read','admin.match-governance.read','admin.match-governance.write','admin.ranked-integrity.read','admin.commands.read','releases.read','releases.runtime.read','tournaments.manage','tournaments.rulings.write'])]
platform.adminApi.usernameChangeRequests=async()=>[{id:'rename-1',accountId:'account-001',currentUsername:'超长当前用户名用于移动端边界验证',requestedUsername:'超长目标用户名用于移动端边界验证',reason:'希望统一公开身份并验证长文本在移动端不会越界。',status:'pending',createdAt:'2026-09-25T08:00:00Z'}]
const qaImage='data:image/svg+xml,%3Csvg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 600 351"%3E%3Crect width="600" height="351" fill="%230b2830"/%3E%3Ccircle cx="90" cy="176" r="70" fill="%23d9b65f"/%3E%3C/svg%3E'
const qaHome={version:1,heroSlides:[{id:'hero-qa',eyebrow:'SEASON 01 / RESPONSIVE QA',title:'后台响应式完整内容验收',summary:'这是一行刻意保留手动换行且长度足以触发局部横向滚动的轮播说明文字，用于确认滚动只停留在预览框内。',footer:'2026-09-28 · 管理后台专项',href:'/news/qa-news',linkLabel:'查看详情',mediaAssetId:'media-hero',enabled:true}],notices:[{id:'notice-qa',label:'响应式验收通知',href:'/news/qa-news',tone:'accent',enabled:true}],newsEyebrow:'NEWS',newsTitle:'资讯一览',newsDescription:'后台编辑块验收',videoEyebrow:'VIDEO',videoTitle:'最新视频',videoDescription:'后台编辑块验收',productEyebrow:'PRODUCTS',productTitle:'产品上新',productDescription:'后台编辑块验收'}
const qaLegal={copyright:'© 2026 Cynic Games',trademark:'十二军团后台响应式验收声明',registration:'沪 ICP 备 QA 号',contactLabel:'联系运营',contactHref:'https://example.test/contact'}
platform.adminApi.getContent=async key=>({key,draftValue:key==='home.composition'?JSON.stringify(qaHome):key==='site.footer'?JSON.stringify(qaLegal):'规则中心公告验收内容',publishedValue:key==='home.composition'?JSON.stringify(qaHome):key==='site.footer'?JSON.stringify(qaLegal):'规则中心公告验收内容',status:'published',version:7,updatedBy:'移动端验收管理员',updatedAt:'2026-09-25T08:00:00Z'})
const qaMedia=[{id:'media-hero',kind:'hero',altText:'响应式验收轮播主视觉',focalX:.5,focalY:.5,originalFormat:'svg',contentHash:'0123456789abcdef0123456789abcdef',desktopUrl:qaImage,mobileUrl:qaImage,thumbnailUrl:qaImage,desktopWidth:600,desktopHeight:351,mobileWidth:390,mobileHeight:520,thumbnailWidth:300,thumbnailHeight:176,originalBytes:4096,deliveryBytes:2048,createdBy:'qa-admin',createdAt:'2026-09-25T08:00:00Z',referenceCount:0,desktopAltText:'桌面轮播验收图',mobileAltText:'移动轮播验收图',thumbnailAltText:'缩略轮播验收图',independentVariants:true},{id:'media-card-art',kind:'card-art',altText:'验收异画素材',focalX:.5,focalY:.5,originalFormat:'svg',contentHash:'abcdef0123456789abcdef0123456789',desktopUrl:qaImage,mobileUrl:qaImage,thumbnailUrl:qaImage,desktopWidth:500,desktopHeight:700,mobileWidth:500,mobileHeight:700,thumbnailWidth:250,thumbnailHeight:350,originalBytes:4096,deliveryBytes:2048,createdBy:'qa-admin',createdAt:'2026-09-25T08:00:00Z',referenceCount:1,desktopAltText:'验收异画',mobileAltText:'验收异画',thumbnailAltText:'验收异画',independentVariants:false}]
platform.adminApi.siteMedia=async kind=>kind?qaMedia.filter(item=>item.kind===kind):qaMedia
platform.adminApi.siteCategories=async()=>[{id:'category-news',kind:'news',name:'官方公告与超长分类名称',slug:'official-news',sortOrder:0,active:true,version:3,itemCount:1},{id:'category-video',kind:'video',name:'官方视频',slug:'official-video',sortOrder:0,active:true,version:2,itemCount:1},{id:'category-product',kind:'product',name:'产品资讯',slug:'products',sortOrder:0,active:true,version:2,itemCount:1}]
platform.adminApi.contentBatches=async()=>[{id:'batch-responsive-qa',action:'publish',status:'published',actorId:'qa-admin',actorName:'移动端验收管理员',createdAt:'2026-09-25T08:00:00Z',items:[{key:'home.composition',previousValue:'{}',publishedValue:JSON.stringify(qaHome),publishedVersionId:'content-v7'}]}]
platform.adminApi.articles=async()=>[{id:'qa-news',title:'后台响应式验收资讯稿件',summary:'用于站点内容编辑器的成功态数据。',body:'正文',category:'官方公告',coverUrl:qaImage,link:'',slug:'qa-news',pinned:true,status:'published',hasUnpublishedChanges:false,createdAt:'2026-09-24T08:00:00Z',updatedAt:'2026-09-25T08:00:00Z',publishedAt:'2026-09-25T08:00:00Z',author:'十二军团官方',updatedBy:'qa-admin',publishedBy:'qa-admin',revision:3,kind:'news',categoryId:'category-news',mediaAssetId:'media-hero',sortOrder:0}]
platform.adminApi.alternateArts=async()=>[{id:'art-qa',artCode:'ALT-QA-001',baseCardId:'S01-02C1',displayName:'移动端验收限定异画',mediaAssetId:'media-card-art',imageUrl:qaImage,thumbnailUrl:qaImage,active:true,createdAt:'2026-09-25T08:00:00Z',updatedAt:'2026-09-25T08:00:00Z',productId:'product-qa',productName:'响应式验收典藏',cardImageId:'art-qa',builtIn:false,baseCardName:'移动端长名称卡效验收卡牌'}]
platform.adminApi.alternateArtGrants=async()=>[{id:'grant-qa',accountId:'account-001',username:'长昵称玩家一号',alternateArtId:'art-qa',sourceKind:'manual',sourceReference:'响应式验收',grantedAt:'2026-09-25T08:00:00Z'}]
platform.adminApi.alternateArtProducts=async()=>[{id:'product-qa',name:'响应式验收典藏',active:true,createdAt:'2026-09-25T08:00:00Z',updatedAt:'2026-09-25T08:00:00Z'}]
platform.adminApi.alternateArtAwardRules=async()=>[{id:'award-qa',alternateArtId:'art-qa',kind:'season-final',seasonId:'S01',eventId:'',minimumTierIndex:2,active:true,createdAt:'2026-09-25T08:00:00Z',updatedAt:'2026-09-25T08:00:00Z'}]
platform.adminApi.rankedIntegrityAudits=async()=>Array.from({length:2},(_,index)=>({id:'integrity-'+index,matchId:'MATCH-INTEGRITY-'+index,seasonId:'S01',firstAccountId:'account-001',firstPlayer:'长昵称玩家一号',secondAccountId:'account-002',secondPlayer:'长昵称玩家二号',winner:0,durationMs:125000,meaningfulCommandCount:18,conclusionKind:'completed',networkLinked:true,networkCorrelationId:'network-correlation-long-'+index,signals:[{code:'unilateral-score-transfer',label:'单向分数转移'}],browserLinked:true,browserCorrelationId:'browser-correlation-long-'+index,finalRound:3,reviewRecommended:true,effectiveDisposition:'unreviewed',enforcement:'held',createdAt:'2026-09-25T08:00:00Z'}))
platform.adminApi.releaseEnvironments=async()=>[{environment:'production',version:20260925,state:'healthy',adapterConfigured:true,activeArtifactId:'artifact-with-a-very-long-production-identifier',activeCommit:'0123456789abcdef0123456789abcdef',health:{success:true,code:'ok',durationMs:42},webSocket:{success:true,code:'ok',durationMs:35},observedAt:'2026-09-25T08:00:00Z'}]
platform.adminApi.releaseArtifacts=async()=>[{id:'artifact-with-a-very-long-production-identifier',commit:'0123456789abcdef0123456789abcdef',releaseSha256:'release-sha256-responsive-qa',verifiedAt:'2026-09-25T08:00:00Z',verificationGates:['build','contracts'],environments:['production']}]
platform.adminApi.releaseRuns=async()=>[]
platform.adminApi.securityStatus=async()=>({activeApprovers:3,secondApproverReady:true,highRiskAuditAvailable:true,auditRetentionDays:365,auditArchiveSegments:4,lastAuditArchiveAt:'2026-09-25T08:00:00Z',disabledAccounts:2,activeLoginLocks:1,pendingApprovals:1,oldestPendingApprovalAt:'2026-09-25T07:00:00Z',platformVersion:20260925,alerts:[{code:'qa-warning',severity:'warning',count:1,message:'响应式验收安全提示'}],offlineBootstrapUsed:false,offlineBootstrapEnabled:false,offlineBootstrapCredentialConfigured:false,mfa:{credentialProtectionAvailable:true,enrollmentEnabled:false,mode:'optional',secretsPersisted:true,requirement:'optional'}})
platform.adminApi.auditArchives=async()=>[]
platform.adminApi.commands=async()=>[{id:'command-very-long-identifier-0123456789abcdef',type:'operations-config-apply',actorId:'qa-admin',actorName:'移动端验收管理员',requestedAt:'2026-09-25T08:00:00Z',scope:'operations',reason:'响应式验收',dryRun:false,risk:'high',status:'executed',permission:'admin.operations.write',payload:{},correlationId:'correlation-responsive-qa',resourceVersion:7,updatedAt:'2026-09-25T08:00:00Z'}]
const operationsConfigPayload={season:{id:'S01',name:'第一赛季·后台响应式验收',status:'active',startsAt:'2026-09-01T00:00:00Z',endsAt:'2026-12-01T00:00:00Z'},disasterPool:{cardIds:['S01-DS01','S01-DS02','S01-DS03','S01-DS04','S01-DS05','S01-DS06','S01-DS07','S01-DS08','S01-DS10'],annihilationLocked:true},cardRestrictions:[{cardId:'S01-02C1',maxCopies:2,reason:'验收长文本限制说明'}],defaultPresetDeckIds:['preset-order-control'],matchModes:[{id:'ranked',name:'排位对战',enabled:true},{id:'casual',name:'休闲对战',enabled:true}],defaultRoomConfig:{matchModeId:'ranked',spectating:'public',handVisibility:'request',disasterMode:'season'},featureFlags:{tournaments:true,publicDecks:true,spectating:true},maintenance:{enabled:true,message:'计划维护期间新对局入口关闭，已有对局继续。',startsAt:'2026-10-01T01:00:00Z',endsAt:'2026-10-01T03:00:00Z',advanceBroadcastHours:24,expectedDurationHours:2},announcements:[{id:'announcement-qa',content:'后台响应式验收长期公告',enabled:true,sortOrder:0}]}
const operationsView={version:37,versionId:'operations-config-responsive-qa-v37',config:operationsConfigPayload,updatedBy:'移动端验收管理员',updatedAt:'2026-09-25T08:00:00Z'}
platform.adminApi.operationsConfig=async()=>structuredClone(operationsView)
platform.adminApi.operationsHistory=async()=>[{id:'operations-history-v36',version:36,action:'apply',config:structuredClone(operationsConfigPayload),actorId:'qa-admin',actorName:'移动端验收管理员',reason:'上一版响应式验收配置',createdAt:'2026-09-24T08:00:00Z'}]
platform.adminApi.runtimeStatus=async()=>({observedAt:'2026-09-25T08:00:00Z',serviceVersion:'2026.09.25-responsive-qa-long-version',cardCount:324,onlineAccountCount:128,webSocketConnectionCount:96,roomCount:24,activeGameCount:18,releaseEnvironments:await platform.adminApi.releaseEnvironments(),cdn:{name:'卡图 CDN',configured:true,state:'healthy',detail:'全部区域可用',observedAt:'2026-09-25T08:00:00Z'},httpPerformance:{windowSeconds:300,slowRequestThresholdMilliseconds:800,minimumSamples:20,minimumReadSamples:10,minimumMutationSamples:5,sampleCount:240,readSampleCount:180,mutationSampleCount:60,diagnosticRequestCount:8,inFlight:3,peakInFlight:18,averageDurationMilliseconds:42,p95LatencyBand:'100-250ms',slowRequestCount:2,slowRequestPercent:.8,rateLimitedCount:1,rateLimitedPercent:.4,serverErrorCount:0,serverErrorPercent:0,expectedUnavailableCount:1,expectedUnavailablePercent:.4,clientCancelledCount:1,clientCancelledPercent:.4,sampleSufficient:true,withinBudget:true,budgetFailures:[]}})
const rankedTier=(name,minimum,index)=>({name,minimum,baseDelta:100+index*10,winStreakCap:50,lossProtectionCap:40,ratingGapCap:30,streakTerminationReward:20,color:'#d7ba63',icon:'◆'})
const rankedFactions=['order','chaos','fate'].map((id,factionIndex)=>({id,name:['秩序','混沌','命运'][factionIndex],color:'#d7ba63',icon:'◆',firstTitle:'派系第一',topFiveTitle:'派系前五',tiers:['新星','星火','群星','璀璨','永恒'].map((name,index)=>rankedTier(name,index*10000,index))}))
platform.adminApi.rankedConfig=async()=>({placementMatches:5,placementMaximum:19999,broadcastEnabled:true,factions:rankedFactions,masterTitles:[{masterId:'S01-0001',masterName:'验收主宰一号',title:'最强验收主宰'}],timeControl:{totalTimeSeconds:1500,operationTimeSeconds:240,reconnectGraceSeconds:240,disasterDecisionSeconds:60,mulliganDecisionSeconds:60},broadcast:{displaySeconds:16,lobbyDelaySeconds:3,intervalSeconds:15,winStreakThreshold:5,streakEndedThreshold:5,minimumTierIndex:0,winStreakEnabled:true,streakEndedEnabled:true,highestTierEnabled:true,factionTitleEnabled:true,masterTitleEnabled:true}})
const seasonRanked=await platform.adminApi.rankedConfig()
const seasonDefinition=(slot,id,name,revision)=>({definitionId:'definition-'+slot,seasonId:id,name,startsAt:'2026-09-01T00:00:00Z',endsAt:'2026-12-01T00:00:00Z',configuration:{disasterPool:structuredClone(operationsConfigPayload.disasterPool),cardRestrictions:structuredClone(operationsConfigPayload.cardRestrictions),defaultPresetDeckIds:[...operationsConfigPayload.defaultPresetDeckIds],ranked:structuredClone(seasonRanked)},lifecycleStatus:slot==='current'?'active':'draft',revision,createdBy:'qa-admin',createdAt:'2026-09-01T00:00:00Z',updatedBy:'qa-admin',updatedAt:'2026-09-25T08:00:00Z'})
platform.adminApi.seasonCatalog=async()=>({current:seasonDefinition('current','S01','第一赛季·后台响应式验收',3),next:seasonDefinition('next','S02','第二赛季超长草稿名称用于响应式验收',1),archives:[],automaticActivationEnabled:false,operationsVersion:37})
platform.adminApi.previewSeasonActivation=async(definitionId,currentRevision,draftRevision,operationsVersion)=>({valid:true,observedAt:'2026-09-25T08:05:00Z',definitionId,currentRevision,draftRevision,operationsVersion,planStatus:'unarmed',planGeneration:0,leaseState:'free',readiness:{seasonId:'S01',activeMatches:0,pendingSettlements:0,appliedReconciliationFailures:0,quarantinedSettlements:0,ready:true},settlementParticipantCount:128,historyRecordCount:128,summaryNotificationCount:128,currentToHistory:{seasonId:'S01',seasonName:'第一赛季·后台响应式验收',fromStatus:'active',toStatus:'history'},nextToCurrent:{seasonId:'S02',seasonName:'第二赛季超长草稿名称用于响应式验收',fromStatus:'draft',toStatus:'active'},rankedAdmissionImpact:'fence-after-arm-at-scheduled-time',rankedAdmissionFencesAt:'2026-12-01T00:00:00Z',blockingCodes:[],suggestedActionCodes:['preview-and-arm'],previewToken:'responsive-impact-token'})
platform.rankedApi.broadcasts=async()=>[{id:'broadcast-qa',matchId:'MATCH-QA-001',eventType:'win-streak',message:'长昵称玩家一号已取得五连胜',createdAt:'2026-09-25T08:00:00Z'}]
const coverage={schemaVersion:2,supportedKinds:[],exactFacts:90,inferredFacts:0,partialFacts:0,exactDeckSnapshots:40,inferredDeckSnapshots:0,privateDuringActiveMatch:false,metrics:[],limitations:['合成验证数据']}
const uncertainty={status:'available',method:'wilson',low:.02,high:.14,reason:null}
const cardItem={cardId:'QA-CARD-LONG-IDENTIFIER',sampleSize:40,eligibleSampleSize:80,includedMatches:40,averageQuantity:2,inclusionRate:.5,wins:22,winRate:.55,winRateConfidence:{low:.4,high:.7},exactDrawCoverageSamples:80,gihSamples:40,gihWins:24,gihWinRate:.6,gihWinRateConfidence:{low:.45,high:.72},gnsSamples:40,gnsWins:19,gnsWinRate:.48,gnsWinRateConfidence:{low:.34,high:.62},inHandWinRateDelta:.12,inHandWinRateDeltaConfidence:{low:.02,high:.22},baselineWinRate:.42,baselineWinRateConfidence:{low:.3,high:.55},winRateDelta:.08,winRateDeltaConfidence:{low:.02,high:.14},drawnMatches:40,playedMatches:40,drawnSamples:40,playedSamples:36,activatedSamples:32,settledSamples:30,resolvedSamples:26,negatedSamples:2,fizzledSamples:2,activatedCount:45,resolvedCount:31,negatedCount:3,fizzledCount:2,coverage,sampleStructure:{participantSamples:40,distinctMatches:40,distinctPlayers:30,knownPlayerSamples:40,anonymousPlayerSamples:0,maximumPlayerContribution:2,maximumPlayerContributionRate:.05,dependencyStatus:'available',uncertainty},comparison:{carriedSamples:40,comparisonSamples:40,insufficientStrata:0,excludedIncludedSamples:0,winRate:.42,delta:.08,weighting:'included-sample',uncertainty},usage:{metrics:[{metric:'draw',observedParticipantSamples:40,eventCount:44,exactFacts:44,inferredFacts:0,partialFacts:0,eligibleSamples:40,coverageStatus:'complete'}]}}
platform.adminApi.cardAnalytics=async query=>({items:[cardItem],total:1,page:query.page||1,pageSize:query.limit||20,summary:{eligibleMatches:80,sampleSize:80,coverage}})
const qaMatchSummary={matchId:'MATCH-QA-RESPONSIVE-001-LONG-IDENTIFIER',modeId:'ranked',status:'completed',players:[{accountId:'account-001',displayName:'长昵称玩家一号',masterId:'S01-0001',deckName:'秩序控制超长构筑名称',result:'win'},{accountId:'account-002',displayName:'长昵称玩家二号',masterId:'S01-0002',deckName:'混沌快攻超长构筑名称',result:'loss'}],startedUtc:'2026-09-25T07:00:00Z',endedUtc:'2026-09-25T07:12:00Z',durationSeconds:720,commandCount:128,error:null}
platform.adminApi.matches=async()=>({items:[qaMatchSummary],total:1,nextCursor:null})
platform.adminApi.playerMatches=async()=>({items:[qaMatchSummary],total:1,nextCursor:null})
platform.adminApi.match=async()=>({summary:qaMatchSummary,participants:qaMatchSummary.players.map((player,index)=>({...player,playerIndex:index,masterName:index?'验收主宰二号':'验收主宰一号',deckCards:[{cardId:'QA-CARD-LONG-IDENTIFIER',quantity:3,section:'main'}],deckSnapshotCoverage:'exact'})),replay:[],cardFacts:[{kind:'draw',commandSequence:12,revision:1,occurredUtc:'2026-09-25T07:03:00Z',playerIndex:0,accountId:'account-001',cardId:'QA-CARD-LONG-IDENTIFIER',coverage:'exact'}],coverage})
const cardBreakdowns=['mode','master','opponent-master','initiative','rules-version','effect-version','season'].map((dimension,index)=>({dimension,value:'responsive-qa-'+dimension+'-long-dimensional-value',sampleSize:40-index,eligibleSampleSize:80,wins:22-index,winRate:.55,winRateConfidence:{low:.4,high:.7},baselineWinRate:.42,baselineWinRateConfidence:{low:.3,high:.55},winRateDelta:.08,winRateDeltaConfidence:{low:.02,high:.14}}))
const masterIds=['S01-0001','S01-0002','S01-0003','S01-0004']
const cardMatchups=masterIds.flatMap((masterId,row)=>masterIds.map((opponentMasterId,column)=>({masterId,opponentMasterId,sampleSize:36+row+column,eligibleSampleSize:72,wins:20+row,winRate:.55,winRateConfidence:{low:.4,high:.7},baselineWinRate:.48,baselineWinRateConfidence:{low:.35,high:.61},winRateDelta:.07,winRateDeltaConfidence:{low:.01,high:.13}})))
platform.adminApi.cardAnalyticsDetail=async()=>({summary:cardItem,breakdowns:cardBreakdowns,quantityDistribution:[{quantity:1,sampleSize:12,wins:6,winRate:.5},{quantity:2,sampleSize:20,wins:12,winRate:.6},{quantity:3,sampleSize:8,wins:4,winRate:.5}],turnDistribution:[{turn:1,firstDrawSamples:8,firstPlaySamples:2},{turn:2,firstDrawSamples:15,firstPlaySamples:10},{turn:3,firstDrawSamples:12,firstPlaySamples:14}],matchups:cardMatchups,recentMatches:[qaMatchSummary],coverage})
const masterItems=masterIds.map((masterId,index)=>({masterId,participantSamples:120-index*8,distinctMatches:60-index*4,distinctDecks:8-index,usageRate:.3-index*.03,deckShare:.28-index*.02,wins:68-index*4,winRate:.567-index*.02,winRateConfidence:{low:.45-index*.01,high:.66-index*.01},averageDurationSeconds:510-index*12,firstSamples:60-index*3,firstWinRate:.58-index*.02,secondSamples:60-index*3,secondWinRate:.55-index*.02}))
platform.adminApi.masterAnalytics=async query=>({items:masterItems,matchups:masterIds.flatMap((masterId,row)=>masterIds.map((opponentMasterId,column)=>({masterId,opponentMasterId,samples:44+row+column,wins:24+row,winRate:.54+row*.01-column*.01}))),trend:query.masterId?Array.from({length:14},(_,index)=>({date:'2026-09-'+String(index+1).padStart(2,'0'),samples:30+index,wins:17+index,winRate:(17+index)/(30+index)})):[],selectedMasterId:query.masterId||null,popularDecks:query.masterId?[{signature:'responsive-qa-deck-signature-long',samples:42,wins:25,winRate:.595,cards:[{cardId:'QA-CARD-LONG-IDENTIFIER',quantity:3,section:'main'}]}]:[],cards:query.masterId?{items:[cardItem],total:1,page:1,pageSize:20,summary:{eligibleMatches:60,sampleSize:120,coverage}}:null})
const governance=await import('/src/l12/matchGovernance.ts')
governance.matchGovernanceAdminApi.drawRequests=async()=>[{id:'draw-request-responsive-qa',matchId:qaMatchSummary.matchId,roomCode:'QA1234',modeId:'ranked',requesterId:'account-001',requesterName:'长昵称玩家一号',responderId:'account-002',responderName:'长昵称玩家二号',reason:'双方对最终结算存在分歧，请管理员复核完整记录。',status:'pending',adminStatus:'reviewing',adminNotes:'已进入响应式验收流程',requestedAt:'2026-09-25T08:00:00Z',history:[{id:'draw-audit-1',action:'created',actorName:'长昵称玩家一号',createdAt:'2026-09-25T08:00:00Z',comment:'申请复核'}]}]
governance.matchGovernanceAdminApi.playerReports=async()=>[{id:'report-responsive-qa',matchId:qaMatchSummary.matchId,roomCode:'QA1234',modeId:'ranked',reporterId:'account-001',reporterName:'长昵称玩家一号',reportedId:'account-002',reportedName:'长昵称玩家二号',description:'举报内容使用较长文本验证治理表单在窄屏完整可达。',status:'reviewing',adminNotes:'复核中',createdAt:'2026-09-25T08:00:00Z',updatedAt:'2026-09-25T08:30:00Z',history:[]}]
const tournamentCounts={registered:16,pendingCheckIn:2,checkedIn:14,active:14,waitlisted:2,dropped:1,removed:1,registrationBanned:1}
const tournamentParticipant=(accountId,username,seed)=>({accountId,username,checkedIn:true,dropped:false,eliminated:false,removed:false,registrationBanned:false,seed,waitlisted:false,tournamentCheckedInAt:'2026-09-25T07:00:00Z',deck:{name:'响应式验收构筑 '+seed,hash:'deck-hash-responsive-'+seed,submittedAt:'2026-09-25T06:00:00Z',lockedAt:'2026-09-25T07:00:00Z',masterId:masterIds[(seed-1)%masterIds.length],cardIds:[],moraleIds:[],specialIds:[]}})
const bracketMatch=(round,index)=>({id:'bracket-'+round+'-'+index,table:index,playerAAccountId:'p'+index,playerAName:'淘汰赛长昵称选手 '+index,playerBAccountId:'p'+(index+8),playerBName:'另一位长昵称选手 '+index,result:index%2?'player-a':undefined,sourceMatchIds:[]})
const qaTournament={id:'tournament-responsive-qa',code:'L12-RESPONSIVE-QA',name:'十二军团后台响应式验收公开赛',organizerAccountId:'qa-admin',organizerName:'移动端验收管理员',referees:[{accountId:'account-002',username:'响应式验收裁判'}],status:'running',format:'swiss-cut',visibility:'public',maxPlayers:32,startAt:'2026-09-30T12:00:00Z',description:'含完整淘汰树、参赛者与桌次的后台赛事成功态。',rules:{ruleset:'2026 响应式验收规则',disasterMode:'season',banList:'无',disasterCardIds:['S01-DS01'],cardRestrictions:[],deckVisibility:'after',ruleContentHash:'rule-content-responsive',hash:'rules-responsive-qa',capturedAt:'2026-09-25T06:00:00Z'},roundMinutes:50,checkInMinutes:10,timeControl:{totalTimeSeconds:1500,operationTimeSeconds:240,reconnectGraceSeconds:240,disasterDecisionSeconds:60,mulliganDecisionSeconds:60},usesLegacyRoundClock:false,counts:tournamentCounts,organizerTransferHistory:[],participants:Array.from({length:8},(_,index)=>tournamentParticipant('p'+(index+1),'参赛选手长昵称 '+(index+1),index+1)),rounds:[],version:12,legacyImported:false,createdAt:'2026-09-01T00:00:00Z',updatedAt:'2026-09-25T08:00:00Z',swissRounds:5,cutSize:8,registrationVisibility:'public',lateGraceMinutes:10,finalSwissStandings:[],eliminationBracket:Array.from({length:5},(_,index)=>({number:index+1,matches:[bracketMatch(index+1,1),bracketMatch(index+1,2)]})),phase:'running',registrationOpen:false,postponements:[],judgeCases:[]}
platform.tournamentApi.list=async()=>({platformVersion:12,items:[structuredClone(qaTournament)]})
platform.tournamentApi.getByCode=async()=>structuredClone(qaTournament)
platform.adminApi.globalAnalytics=async()=>({fromDate:'2026-09-19',toDate:'2026-09-25',days:Array.from({length:7},(_,index)=>({date:'2026-09-'+String(19+index).padStart(2,'0'),dailyActiveUsers:20+index,weeklyActiveUsers:90+index,monthlyActiveUsers:180+index,dailyMatches:10+index,weeklyMatches:70+index,monthlyMatches:300+index,averageOnline:5.2+index/10,peakOnline:12+index,peakOnlineAt:'2026-09-25T03:30:00Z',newUsers:3+index,returningUsers:20+index,pageViews:200+index*10})),pageViews:[{path:'/battle/rankings?responsive=very-long-query-value',views:188},{path:'/admin/operations/global',views:96}]})
const qaOriginalFetch=window.fetch.bind(window)
window.fetch=async(input,init)=>{
 const url=String(typeof input==='string'?input:input.url)
 if(url.includes('/api/operations/effective-policy'))return new Response(JSON.stringify({version:37,season:operationsConfigPayload.season,disasterCardIds:operationsConfigPayload.disasterPool.cardIds,matchModes:operationsConfigPayload.matchModes,defaultRoomConfig:operationsConfigPayload.defaultRoomConfig,seasonDisasterModeAvailable:true,cardRestrictions:operationsConfigPayload.cardRestrictions,defaultPresetDeckIds:operationsConfigPayload.defaultPresetDeckIds,maintenance:{...operationsConfigPayload.maintenance,active:false,entryBlocked:false,status:'upcoming',broadcastMessage:operationsConfigPayload.maintenance.message},announcements:operationsConfigPayload.announcements}),{status:200,headers:{'Content-Type':'application/json'}})
 return qaOriginalFetch(input,init)
}
`

const cutStart = baseEntry.indexOf("const mode=new URLSearchParams")
const beforeMode = baseEntry.slice(0, cutStart)
const entry = beforeMode + extraMocks + imports

const routePaths = [
 '/admin','/admin/users/accounts','/admin/users/accounts/account-001','/admin/users/renames','/admin/users/bugs',
 '/admin/content/site','/admin/content/rules','/admin/content/effects','/admin/content/alternate-arts',
 '/admin/matches/archive','/admin/matches/governance','/admin/matches/integrity','/admin/matches/tournaments',
 '/admin/operations/config','/admin/operations/global','/admin/operations/cards',
 '/admin/system/releases','/admin/system/security','/admin/system/storage','/admin/system/commands','/admin/system/audit',
]
const viewports=[
 {width:320,height:568},{width:360,height:800},{width:390,height:844},{width:430,height:932},{width:768,height:1024},
 {width:1024,height:768},{width:1280,height:720},{width:1366,height:768},{width:1440,height:900},{width:1920,height:1080},{width:2560,height:1080},
]

let browser
const server=await createServer({root,configLoader:'runner',cacheDir:path.join(root,'.tmp','vite-admin-responsive-full-audit'),server:{host:'127.0.0.1',port:0,strictPort:false},plugins:[{
 name:'full-admin-responsive-audit',resolveId(id){if(id==='/__admin_audit__.js')return id},load(id){if(id==='/__admin_audit__.js')return entry},
 configureServer(devServer){devServer.middlewares.use((request,response,next)=>{if(request.url?.match(/^\/__admin_audit__(\?|$)/)){response.setHeader('Content-Type','text/html');response.end('<style>html,body,#app{margin:0;min-height:100%;background:#080d11;color:#eee}</style><div id="app"></div><script type="module" src="/@vite/client"></script><script type="module" src="/__admin_audit__.js"></script>');return}next()})}
}]})

function slug(route){return route.replace(/^\/admin\/?/,'').replaceAll('/','--')||'overview'}
const readyText={
 '/admin':'待处理事项','/admin/users/accounts':'长昵称玩家一号','/admin/users/accounts/account-001':'基本资料',
 '/admin/users/renames':'超长目标用户名','/admin/users/bugs':'移动端弹框在极窄屏幕下信息显示不完整',
 '/admin/content/rules':'规则中心与 FAQ 裁定库','/admin/content/effects':'移动端长名称卡效验收卡牌',
 '/admin/content/alternate-arts':'响应式验收典藏','/admin/matches/archive':'MATCH-QA-RESPONSIVE-001-LONG-IDENTIFIER',
 '/admin/matches/governance':'draw-request-responsive-qa','/admin/matches/integrity':'MATCH-INTEGRITY-0',
 '/admin/operations/config':'operations-config-responsive-qa-v37','/admin/operations/global':'逐日历史',
 '/admin/system/releases':'运行态只读快照','/admin/system/security':'qa-warning',
 '/admin/system/storage':'存储容量需要关注','/admin/system/commands':'command-very-long-identifier-0123456789abcdef',
 '/admin/system/audit':'account-001',
}
async function assertNoPendingOrError(page,route){
 const bad=await page.locator('.admin-content').evaluate((root)=>[...root.querySelectorAll('*')].filter(el=>{
  const style=getComputedStyle(el);if(style.display==='none'||style.visibility==='hidden'||!el.getClientRects().length)return false
  const text=(el.textContent||'').trim();return /^(等待加载|加载中…?|读取中…?|正在读取卡牌数据…|正在读取卡牌清单…|正在从服务端读取赛事…)$/.test(text)||/(加载失败|读取失败|请求失败)/.test(text)
 }).slice(0,12).map(el=>(el.textContent||'').trim()))
 assert.equal(bad.length,0,route+' pending/error state: '+bad.join(' | '))
}
async function prepareRoute(page,route){
 await page.evaluate(path=>window.__qaRouter.push(path),route)
 await page.locator('.admin-content').waitFor({state:'visible'})
 if(route==='/admin/content/site'){
  await page.getByRole('button',{name:'刷新全部'}).waitFor({state:'visible'})
  const nav=page.locator('.site-content-nav button');await nav.first().waitFor({state:'visible'});assert.equal(await nav.count(),11,'site content navigation count')
  for(let index=0;index<11;index++)assert.equal(await nav.nth(index).isVisible(),true,'site content navigation button '+index+' visible')
  await page.getByRole('button',{name:'轮播图',exact:true}).click()
  await page.getByText('首页轮播图',{exact:true}).waitFor({state:'visible'})
  await page.locator('.hero-compose-row').waitFor({state:'visible'})
 }else if(route==='/admin/matches/tournaments'){
  await page.getByText('十二军团后台响应式验收公开赛',{exact:true}).waitFor({state:'visible'})
  await page.getByRole('button',{name:'赛事详情'}).click()
  await page.locator('.bracket').waitFor({state:'visible'})
 }else if(route==='/admin/operations/cards'){
  await page.getByRole('button',{name:'卡牌数据清单'}).click()
  await page.locator('.card-list .card-row').first().waitFor({state:'visible'})
  await page.locator('.card-list .card-row').first().click()
  await page.locator('.analysis-detail .card-heading').waitFor({state:'visible'})
  await page.locator('.matchup-panel').evaluate(el=>{el.open=true})
  await page.locator('.breakdowns').evaluate(el=>{el.open=true})
 }else{
  const marker=readyText[route]
  if(!marker)throw new Error('Missing explicit route readiness marker for '+route)
  await page.getByText(marker,{exact:false}).first().waitFor({state:'visible',timeout:15000})
 }
 if(route==='/admin/operations/config'){
  await page.getByLabel('赛季 ID').waitFor({state:'visible'})
  await page.getByRole('button',{name:'预览赛季定义'}).waitFor({state:'visible'})
  await page.getByRole('button',{name:'预览切季影响'}).click();await page.getByText('切季影响已锁定').waitFor({state:'visible'});await page.getByPlaceholder('预约或取消预约的理由（必填）').fill('响应式验收预约理由');await page.getByRole('button',{name:'确认预约'}).click();await page.locator('.admin-risk-dialog').waitFor({state:'visible'});await page.getByRole('dialog').getByRole('button',{name:'取消'}).click()
  await page.getByRole('button',{name:/排位与七曜/}).click();await page.getByLabel('定级场次').waitFor({state:'visible'});await page.getByText('近期排位快讯',{exact:true}).waitFor({state:'visible'});assert.equal(await page.getByText('暂无排位快讯',{exact:true}).count(),0,'ranked broadcast success fixture')
  await page.getByRole('button',{name:/版本与状态/}).click();await page.getByText('2026.09.25-responsive-qa-long-version',{exact:true}).waitFor({state:'visible'});await page.getByText('上一版响应式验收配置',{exact:true}).waitFor({state:'visible'})
  await page.getByRole('button',{name:/赛季与天灾/}).click();await page.getByLabel('赛季 ID').waitFor({state:'visible'})
 }
 if(route==='/admin/operations/global'){
  assert.ok(await page.locator('.history .row:not(.head)').count()>=7,'global daily data rows')
 }
 if(route==='/admin/system/releases'){
  assert.equal(await page.getByLabel('已验证工件').inputValue(),'artifact-with-a-very-long-production-identifier','release artifact success fixture')
  await page.locator('.panel article code').filter({hasText:'artifact-with-a-very-long-production-identifier'}).waitFor({state:'visible'})
 }
 await assertNoPendingOrError(page,route)
}
async function collectMetrics(page){return page.evaluate(()=>{
 const doc=document.documentElement;const content=document.querySelector('.admin-content');const adminPage=document.querySelector('.admin-page');const allowed='.table,.heat-scroll,.breakdowns,.bracket,.matrix-scroll-access,.master-matchup-matrix,.hero-copy-preview';const items=[...document.querySelectorAll('body *')].map((el,index)=>{const r=el.getBoundingClientRect();const s=getComputedStyle(el);return {index,tag:el.tagName.toLowerCase(),className:typeof el.className==='string'?el.className:'',left:Math.round(r.left),right:Math.round(r.right),width:Math.round(r.width),clientWidth:el.clientWidth,scrollWidth:el.scrollWidth,overflowX:s.overflowX}}).filter(x=>x.right>innerWidth+1||x.left<-1||x.scrollWidth>x.clientWidth+1).slice(0,24);const unreachable=[...document.querySelectorAll('button,input,select,textarea,a,dialog,[role="dialog"]')].filter(el=>{const r=el.getBoundingClientRect();const s=getComputedStyle(el);return s.display!=='none'&&s.visibility!=='hidden'&&r.width>0&&r.height>0&&!el.closest(allowed)&&(r.left<-1||r.right>innerWidth+1)}).map(el=>({tag:el.tagName.toLowerCase(),className:typeof el.className==='string'?el.className:'',text:(el.textContent||'').trim().slice(0,80)}));return {innerWidth,docClientWidth:doc.clientWidth,docScrollWidth:doc.scrollWidth,bodyScrollWidth:document.body.scrollWidth,overflow:doc.scrollWidth>innerWidth+1,contentClientWidth:content?.clientWidth??0,contentScrollWidth:content?.scrollWidth??0,contentOverflow:(content?.scrollWidth??0)>(content?.clientWidth??0)+1,adminPageWidth:Math.round(adminPage?.getBoundingClientRect().width??0),unreachable,items}
})}
async function inspectLocalScroll(page,selector,label,viewport){
 const value=await page.locator(selector).evaluate((el)=>{const content=document.querySelector('.admin-content');return {clientWidth:el.clientWidth,scrollWidth:el.scrollWidth,localOverflow:el.scrollWidth>el.clientWidth+1,contentClientWidth:content?.clientWidth??0,contentScrollWidth:content?.scrollWidth??0,documentOverflow:document.documentElement.scrollWidth>innerWidth+1,tabIndex:el.tabIndex,ariaLabel:el.getAttribute('aria-label')||'',hint:(el.previousElementSibling?.textContent||'').trim()}})
 assert.equal(value.documentOverflow,false,label+' lifted document at '+viewport)
 assert.ok(value.contentScrollWidth<=value.contentClientWidth+1,label+' lifted admin content at '+viewport)
 assert.ok(value.tabIndex>=0,label+' is not keyboard reachable at '+viewport)
 assert.ok(value.ariaLabel.length>0,label+' has no aria label at '+viewport)
 return {label,viewport,...value}
}
try{
 await server.listen();const port=server.httpServer.address().port
 browser=await chromium.launch({headless:true,channel:'msedge'})
 const page=await browser.newPage();const pageErrors=[];page.on('pageerror',error=>pageErrors.push(error.message))
 await page.route('**/*',route=>new URL(route.request().url()).hostname==='127.0.0.1'?route.continue():route.abort())
 const report=[]
 for(const viewport of viewports){
  await page.setViewportSize(viewport)
  await page.goto(`http://127.0.0.1:${port}/__admin_audit__`)
  await page.locator('.admin-shell').waitFor()
  for(const route of routePaths){
   await prepareRoute(page,route)
    const metrics=await collectMetrics(page)
   const item={viewport:`${viewport.width}x${viewport.height}`,route,...metrics};report.push(item)
   if(metrics.overflow||[320,390,768,1280,2560].includes(viewport.width)) await page.screenshot({path:path.join(output,`${slug(route)}-${viewport.width}x${viewport.height}.png`),fullPage:true})
  }
 }
 const zoomChecks=[]
 for(const zoom of [1.25,1.5])for(const physical of [{width:390,height:844},{width:1280,height:720}]){
  const effective={width:Math.floor(physical.width/zoom),height:Math.floor(physical.height/zoom)};await page.setViewportSize(effective);await page.goto(`http://127.0.0.1:${port}/__admin_audit__`);await page.locator('.admin-shell').waitFor()
   for(const route of routePaths){await prepareRoute(page,route);const metrics=await collectMetrics(page);zoomChecks.push({route,zoom,physical,effective,...metrics})}
  }
  const contentCoverage=[]
  const riskDialogs=[]
  for(const viewport of viewports.filter(item=>[320,390,768,1280,2560].includes(item.width))){
   const viewportName=viewport.width+'x'+viewport.height;await page.setViewportSize(viewport);await page.goto(`http://127.0.0.1:${port}/__admin_audit__`);await page.locator('.admin-shell').waitFor()
   await prepareRoute(page,'/admin/operations/global');contentCoverage.push(await inspectLocalScroll(page,'.table','global daily table',viewportName))
   await prepareRoute(page,'/admin/content/site');contentCoverage.push(await inspectLocalScroll(page,'.hero-copy-preview','hero copy preview',viewportName))
   await page.evaluate(path=>window.__qaRouter.push(path),'/admin');await page.getByText('待处理事项',{exact:true}).waitFor({state:'visible'});await page.evaluate(path=>window.__qaRouter.push(path),'/admin/content/site');await page.getByRole('button',{name:'刷新全部'}).waitFor({state:'visible'});await page.locator('.media-grid b').filter({hasText:'响应式验收轮播主视觉'}).waitFor({state:'visible'});await page.getByRole('button',{name:'软删除素材组'}).click();await page.locator('.admin-risk-dialog').waitFor({state:'visible'})
   const dialog=await page.locator('.admin-risk-dialog').evaluate(el=>{const r=el.getBoundingClientRect();const controls=[...el.querySelectorAll('button,input,select,textarea,a')].map(node=>{const b=node.getBoundingClientRect();return {left:b.left,right:b.right,top:b.top,bottom:b.bottom}});return {left:r.left,right:r.right,top:r.top,bottom:r.bottom,innerWidth,innerHeight,controlsReachable:controls.every(b=>b.left>=-1&&b.right<=innerWidth+1&&b.top>=-1&&b.bottom<=innerHeight+1)}})
   assert.ok(dialog.left>=-1&&dialog.right<=dialog.innerWidth+1&&dialog.top>=-1&&dialog.bottom<=dialog.innerHeight+1&&dialog.controlsReachable,'risk dialog unreachable at '+viewportName);riskDialogs.push({viewport:viewportName,...dialog});await page.screenshot({path:path.join(output,'risk-dialog-'+viewportName+'.png'),fullPage:true});await page.getByRole('button',{name:'取消'}).click()
   await prepareRoute(page,'/admin/operations/config');await page.getByRole('button',{name:'预览切季影响'}).click();await page.getByText('切季影响已锁定').waitFor({state:'visible'});await page.getByPlaceholder('预约或取消预约的理由（必填）').fill('响应式危险弹框验收');await page.getByRole('button',{name:'确认预约'}).click();await page.locator('.admin-risk-dialog').waitFor({state:'visible'});const activationDialog=await page.locator('.admin-risk-dialog').evaluate(el=>{const r=el.getBoundingClientRect();const controls=[...el.querySelectorAll('button,input,select,textarea,a')].map(node=>{const b=node.getBoundingClientRect();return {left:b.left,right:b.right,top:b.top,bottom:b.bottom}});return {left:r.left,right:r.right,top:r.top,bottom:r.bottom,innerWidth,innerHeight,controlsReachable:controls.every(b=>b.left>=-1&&b.right<=innerWidth+1&&b.top>=-1&&b.bottom<=innerHeight+1)}});assert.ok(activationDialog.left>=-1&&activationDialog.right<=activationDialog.innerWidth+1&&activationDialog.top>=-1&&activationDialog.bottom<=activationDialog.innerHeight+1&&activationDialog.controlsReachable,'season activation dialog unreachable at '+viewportName);riskDialogs.push({viewport:viewportName,kind:'season-activation',...activationDialog});await page.screenshot({path:path.join(output,'season-activation-risk-'+viewportName+'.png'),fullPage:true});await page.getByRole('dialog').getByRole('button',{name:'取消'}).click()
   await prepareRoute(page,'/admin/operations/cards');contentCoverage.push(await inspectLocalScroll(page,'.heat-scroll','card matchup heatmap',viewportName));contentCoverage.push(await inspectLocalScroll(page,'.breakdowns','card breakdown details',viewportName))
   await page.getByRole('button',{name:'主宰',exact:true}).click();await page.locator('.master-list>button').first().waitFor({state:'visible'});await page.locator('.master-list>button').first().click();await page.locator('.matrix-scroll-access').waitFor({state:'visible'});contentCoverage.push(await inspectLocalScroll(page,'.matrix-scroll-access','master matchup matrix',viewportName));await page.screenshot({path:path.join(output,'operations--cards-master-'+viewportName+'.png'),fullPage:true})
   await prepareRoute(page,'/admin/matches/tournaments');contentCoverage.push(await inspectLocalScroll(page,'.bracket','tournament bracket',viewportName))
  }
  const failures=report.filter(item=>item.overflow||item.contentOverflow||item.unreachable.length||item.adminPageWidth>2000)
  const zoomFailures=zoomChecks.filter(item=>item.overflow||item.contentOverflow||item.unreachable.length)
  const uniqueErrors=[...new Set(pageErrors)]
  const result={status:failures.length||zoomFailures.length||uniqueErrors.length?'failed':'passed',routePaths,viewports,report,zoomChecks,contentCoverage,riskDialogs,pageErrors:uniqueErrors,failures,zoomFailures}
  fs.writeFileSync(path.join(output,'report.json'),JSON.stringify(result,null,2))
  assert.equal(uniqueErrors.length,0,`page errors: ${uniqueErrors.join(' | ')}`)
  assert.equal(failures.length,0,`responsive failures: ${JSON.stringify(failures.slice(0,5))}`)
  assert.equal(zoomFailures.length,0,`zoom failures: ${JSON.stringify(zoomFailures.slice(0,5))}`)
  console.log(JSON.stringify({status:'passed',routes:routePaths.length,viewports:viewports.length,zoomChecks:zoomChecks.length,output},null,2))
}finally{await browser?.close();await server.close()}
